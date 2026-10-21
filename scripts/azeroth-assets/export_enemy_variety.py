"""Export eight approved original creatures with every LOD0 material pass.

User-owned MPQs are read-only. Generated game assets must remain private.
Requires the existing enemy_fidelity_{m2,mpq,blp} helpers, NumPy, Pillow, mpyq.
"""
import argparse, hashlib, json, math, os, struct
from pathlib import Path
import numpy as np
from PIL import Image
from enemy_fidelity_mpq import Archives
from enemy_fidelity_m2 import arr, Track, tr, qm
from enemy_fidelity_blp import decode_blp

ROSTER = [
    ('ghoul',137,[6],False),('zombie',1200,[41],False),
    ('scorpion',2485,[26,30],False),('silithidwasp',92,[24],False),
    ('ent',864,[19,20,21],False),('fleshgolem',1061,[31],False),
    ('doomguard',433,[7],True),('dragonspawncaster',6761,[35,36],True),
]

def digest(data): return hashlib.sha256(data).hexdigest()
def write_json(path, obj): path.write_text(json.dumps(obj,separators=(',',':'))+'\n',encoding='utf-8')
def canonical(indices):
    triples=sorted(tuple(sorted(indices[i:i+3])) for i in range(0,len(indices),3))
    return digest(b''.join(struct.pack('<III',*tri) for tri in triples))
def curve(b,p,fmt):
    interp,glob=struct.unpack_from('<Hh',b,p)
    assert interp in (0,1)
    return dict(interpolation=interp,globalSequence=glob,ranges=arr(b,p+4,'II'),times=[v[0] for v in arr(b,p+12,'I')],values=arr(b,p+20,fmt))
def read_dbc(archives,name):
    data,arc=archives.read('DBFilesClient\\'+name+'.dbc')
    count,fields,size,ss=struct.unpack_from('<4I',data,4);strings=data[20+count*size:]
    def string(offset): return strings[offset:strings.index(b'\0',offset)].decode('utf-8')
    rows=[struct.unpack_from('<'+'I'*fields,data,20+i*size) for i in range(count)]
    return rows,string,dict(archive=arc,sha256=digest(data),records=count,fields=fields)

def export(archives,models,mstr,displays,dstr,key,display,native_ids,boss,output):
    row=next(r for r in displays if r[0]==display);mr=next(r for r in models if r[0]==row[1]);path=mstr(mr[2]);path=path[:-4]+'.m2' if path.lower().endswith('.mdx') else path
    skins=[dstr(x) for x in row[6:9]];b,arc=archives.read(path)
    assert b[:4]==b'MD20' and struct.unpack_from('<I',b,4)[0]==256
    out=output/key;out.mkdir(parents=True,exist_ok=True)
    vertices=arr(b,68,'3f4B4B3f2f2f');original_count=len(vertices)
    positions=np.asarray([v[:3] for v in vertices]);normals=np.asarray([v[11:14] for v in vertices]);uv=np.asarray([v[14:16] for v in vertices]);uv[:,1]=1-uv[:,1]
    weights=np.asarray([v[3:7] for v in vertices])/255.;bone_indices=np.asarray([v[7:11] for v in vertices])
    assert np.isfinite(positions).all() and np.isfinite(normals).all() and np.isfinite(uv).all()
    assert all(sum(v[3:7])==255 for v in vertices)
    _,view=struct.unpack_from('<II',b,76);lookup=arr(b,view,'H');original_indices=[lookup[t[0]][0] for t in arr(b,view+8,'H')]
    assert min(original_indices)>=0 and max(original_indices)<original_count
    submeshes=arr(b,view+24,'10H3f');units=arr(b,view+32,'12H');materials=arr(b,132,'HH')
    tex_lookup=arr(b,148,'H');uv_lookup=arr(b,156,'h');trans_lookup=arr(b,164,'H');texanim_lookup=arr(b,172,'h')
    texture_meta=arr(b,92,'4I');colors_n,colors_off=struct.unpack_from('<II',b,84);alpha_n,alpha_off=struct.unpack_from('<II',b,100)
    globals=[v[0] for v in arr(b,20,'I')];all_anims=arr(b,28,'HHIIfIhHIII7fhH')
    nb,bo=struct.unpack_from('<II',b,52);bones=[]
    for i in range(nb):
        p=bo+i*108;bones.append(dict(parent=struct.unpack_from('<h',b,p+8)[0],flags=struct.unpack_from('<I',b,p+4)[0],pivot=np.asarray(struct.unpack_from('<3f',b,p+96)),t=Track(b,p+12,3,[0,0,0]),r=Track(b,p+40,4,[0,0,0,1],True),s=Track(b,p+68,3,[1,1,1])))
    assert max(bone_indices.reshape(-1))<nb
    textures={};passes=[];vertex_map=list(range(original_count));environment=[]
    for pi,u in enumerate(units):
        si=u[2];s=submeshes[si];raw=original_indices[s[4]:s[4]+s[5]];ti=tex_lookup[u[8]][0];mode=uv_lookup[u[9]][0];fl,blend=materials[u[5]]
        assert mode in (0,-1) and blend in (0,1,2,4)
        assert texanim_lookup[u[11]][0]==-1, 'Animated UVs need a separate verified adapter'
        if ti not in textures:
            typ,tflags,length,offset=texture_meta[ti]
            tp=b[offset:offset+length].rstrip(b'\0').decode() if typ==0 else path.rsplit('\\',1)[0]+'\\'+skins[typ-11]+'.blp' if typ in(11,12,13) and skins[typ-11] else None
            assert tp, (path,ti,typ)
            tb,ta=archives.read(tp);im=decode_blp(tb).convert('RGBA');name='texture' if not textures else 'texture-'+str(ti)
            im.save(out/(name+'.png'));rgba=im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes();(out/(name+'.rgba')).write_bytes(rgba)
            write_json(out/(name+'.rgba.json'),dict(width=im.width,height=im.height,format='RGBA32',rowOrder='bottom-up'))
            hist=im.getchannel('A').histogram();(out/('original-texture-'+str(ti)+'.blp')).write_bytes(tb)
            textures[ti]=dict(id=ti,file=name+'.rgba',width=im.width,height=im.height,flags=tflags,sourcePath=tp,sourceArchive=ta,sourceSha256=digest(tb),rgbaSha256=digest(rgba),alphaZeroPixels=hist[0],alphaCutoutPixels=sum(hist[:128]),alphaOpaquePixels=hist[255],pixels=im.width*im.height)
        mapped=raw
        if mode==-1:
            remap={}
            for vi in raw:
                if vi not in remap:remap[vi]=len(vertex_map);vertex_map.append(vi);environment.append(remap[vi])
            mapped=[remap[vi] for vi in raw]
        indices=np.asarray(mapped).reshape(-1,3)[:,[0,2,1]].reshape(-1).tolist()
        ci=u[4] if u[4]<32768 else u[4]-65536;ai=trans_lookup[u[10]][0]
        passes.append(dict(sourcePass=pi,sourceSubmesh=si,sourceGeosetId=submeshes[si][0],sourceMaterialIndex=u[5],flags=fl,blend=blend,textureId=ti,textureCoordMode=mode,sourceIndexCount=len(raw),sourceCanonicalTriangleSha256=canonical(raw),triangles=indices,color=curve(b,colors_off+ci*56,'3f') if 0<=ci<colors_n else None,alpha=curve(b,colors_off+ci*56+28,'h') if 0<=ci<colors_n else None,weight=curve(b,alpha_off+ai*28,'h') if 0<=ai<alpha_n else None))
    assert {v for p in passes for v in (vertex_map[i] for i in p['triangles'])}==set(original_indices), 'A source triangle section was omitted'
    billboard_bones={i for i,bn in enumerate(bones) if bn['flags']&8}
    billboard_vertices={i for i,v in enumerate(vertices) if any(w>0 and bone in billboard_bones for w,bone in zip(v[3:7],v[7:11]))}
    billboard_quads=[]
    for material_pass in passes:
        ts=[material_pass['triangles'][i:i+3] for i in range(0,len(material_pass['triangles']),3) if all(vertex_map[v] in billboard_vertices for v in material_pass['triangles'][i:i+3])]
        remaining={i for t in ts for i in t}
        while remaining:
            connected={next(iter(remaining))}
            for _ in range(len(remaining)):
                connected|={v for t in ts if connected.intersection(t) for v in t}
            assert len(connected)==4, 'Unverified non-quad source billboard geometry'
            ids=sorted(connected);t=next(t for t in ts if set(t)<=connected);x,y,z=t
            determinant=(uv[vertex_map[y],0]-uv[vertex_map[x],0])*(uv[vertex_map[z],1]-uv[vertex_map[x],1])-(uv[vertex_map[y],1]-uv[vertex_map[x],1])*(uv[vertex_map[z],0]-uv[vertex_map[x],0])
            assert abs(determinant)>1e-9
            billboard_quads.append(dict(vertices=ids,uvDeterminantSign=1 if determinant>0 else -1));remaining-=connected
    source_map=np.asarray(vertex_map);positions=positions[source_map];normals=normals[source_map];uv=uv[source_map];weights=weights[source_map];bone_indices=bone_indices[source_map]
    def unity(v):return np.stack([-v[:,1],v[:,2],v[:,0]],axis=1)
    def pose(time,sequence):
        cache={}
        def world(i):
            if i in cache:return cache[i]
            bn=bones[i];pv=bn['pivot'];m=tr(pv)@tr(bn['t'].sample(time,sequence,globals))@qm(bn['r'].sample(time,sequence,globals))@np.diag([*bn['s'].sample(time,sequence,globals),1])@tr(-pv)
            cache[i]=world(bn['parent'])@m if bn['parent']>=0 else m;return cache[i]
        mats=np.asarray([world(i) for i in range(nb)]);hom=np.concatenate([positions,np.ones((len(positions),1))],axis=1)
        return unity(np.sum(np.einsum('vbij,vj->vbi',mats[bone_indices],hom)[:,:,:3]*weights[:,:,None],axis=1))
    clips=[];death=[];all_min=np.full(3,np.inf);all_max=-all_min
    chosen=[(label,next((i for i,an in enumerate(all_anims) if an[0]==aid),None)) for label,aid in [('idle',0),('walk',4),('run',5),('attack',16)]]
    chosen += [('death' if j==0 else 'death_variant_'+str(i),i) for j,i in enumerate(i for i,an in enumerate(all_anims) if an[0]==1)]
    assert all(i is not None for _,i in chosen)
    for label,seq in chosen:
        an=all_anims[seq];start,end=an[2:4];duration=(end-start)/1000;count=max(2,math.ceil(duration*24)+1)
        assert duration>0
        frames=np.asarray([pose(start+(end-start)*f/(count-1),seq) for f in range(count)],dtype='<f4');assert np.isfinite(frames).all()
        file=label+'.vertices.bin';payload=frames.tobytes();(out/file).write_bytes(payload);all_min=np.minimum(all_min,frames.min(axis=(0,1)));all_max=np.maximum(all_max,frames.max(axis=(0,1)))
        clip=dict(name=label,file=file,animationId=an[0],sequenceIndex=seq,frameCount=count,vertexCount=len(vertex_map),duration=duration,fps=(count-1)/duration,sourceStartMs=start,sourceEndMs=end,sourceFrequency=max(0,an[6]),loop=label in('idle','walk','run'),maxVertexDisplacement=float(np.max(np.linalg.norm(frames-frames[0],axis=2))),sha256=digest(payload))
        clips.append(clip)
        if an[0]==1:death.append(dict(clip,sourceSequenceSha256=digest(struct.pack('<HHIIfIhHIII7fhH',*an))))
    idle=next(c for c in clips if c['name']=='idle');base=pose(idle['sourceStartMs'],idle['sequenceIndex'])
    flat_triangles=np.asarray(original_indices).reshape(-1,3)[:,[0,2,1]].reshape(-1).tolist()
    model=dict(format='azeroth-unity-mesh-v1',name=key,coordinateSystem='source(x,y,z) -> Unity(-y,z,x), V flipped; original winding reversed',vertexCount=len(vertex_map),positions=base.reshape(-1).tolist(),normals=unity(normals).reshape(-1).tolist(),uv=uv.reshape(-1).tolist(),triangles=flat_triangles,sourceVertexMap=vertex_map,animations=clips,boundsMin=all_min.tolist(),boundsMax=all_max.tolist(),source=dict(model=path,archive=arc,sha256=digest(b),version=256,displayId=display,skin=skins,originalVertexCount=original_count,originalTriangleCount=len(original_indices)//3,boneCount=nb,submeshCount=len(submeshes),materialPassCount=len(passes),reflectionVertexDuplicates=len(environment)))
    profile=dict(format='azeroth-enemy-material-profile-v1',sourceSha256=digest(b),sourceDisplayId=display,originalVertexCount=original_count,sourceVertexMap=vertex_map,globals=globals,textures=list(textures.values()),passes=passes,environmentVertexIndices=environment,billboardQuads=billboard_quads,clips={c['name']:dict(sequenceIndex=c['sequenceIndex'],sourceStartMs=c['sourceStartMs'],sourceEndMs=c['sourceEndMs'],frameCount=c['frameCount']) for c in clips})
    write_json(out/'model.json',model);write_json(out/'source-material-profile.json',profile);(out/'original.m2').write_bytes(b)
    write_json(out/'death-profile.json',dict(format='azeroth-enemy-death-profile-v1',sourceModel=path,sourceSha256=digest(b),lifetimeSeconds=1.2,lifetimeEvidence='VA14593FBC6/FC43..FC5B',animations=death))
    info=dict(key=key,displayId=display,nativeEnemyIds=native_ids,bossOnly=boss,sourcePath=path,sourceArchive=arc,sourceSha256=digest(b),originalVertexCount=original_count,exportVertexCount=len(vertex_map),originalTriangleCount=len(original_indices)//3,sourcePassCount=len(passes),reflectionVertexDuplicates=len(environment),sourceBoneBillboardFlags=sorted(set(bn['flags']&0x78 for bn in bones if bn['flags']&0x78)),sourceBillboardQuadCount=len(billboard_quads),sourceTextures=list(textures.values()),sourcePasses=[{k:v for k,v in p.items() if k not in('triangles','color','alpha','weight')} for p in passes],animations=[{k:v for k,v in c.items() if k!='file'} for c in clips],files=[dict(name=p.name,sha256=digest(p.read_bytes()),bytes=p.stat().st_size) for p in sorted(out.iterdir()) if p.is_file()])
    print(json.dumps(dict(key=key,vertices=len(vertex_map),passes=len(passes),textures=len(textures),clips=len(clips))),flush=True)
    return info

def main():
    p=argparse.ArgumentParser(__doc__);p.add_argument('--data-dir',type=Path,default=os.environ.get('AZEROTH_DATA_DIR'));p.add_argument('--output-root',type=Path,required=True);a=p.parse_args()
    if not a.data_dir:p.error('--data-dir or AZEROTH_DATA_DIR is required')
    output=a.output_root.resolve();data=a.data_dir.resolve(strict=True)
    if output==data or data in output.parents:raise ValueError('Output must not be inside original game archive directory')
    output.mkdir(parents=True,exist_ok=True)
    with Archives(data) as archives:
        models,ms,mi=read_dbc(archives,'CreatureModelData');displays,ds,di=read_dbc(archives,'CreatureDisplayInfo')
        entries=[export(archives,models,ms,displays,ds,*args,output) for args in ROSTER]
    write_json(output/'enemy-variety-assets.json',dict(format='azeroth-enemy-variety-assets-v1',sourceTables=dict(CreatureModelData=mi,CreatureDisplayInfo=di),models=entries,limitations=['Lighting and camera-space reflection are Unity render adapters, not the original client shader.','The existing native-driven live visual loop selects idle/run; source walk/attack/death sequences are retained separately.','Source bone billboarding, if any, is reported explicitly. No embedded model emitters exist in these8models.']))

if __name__=='__main__':main()
