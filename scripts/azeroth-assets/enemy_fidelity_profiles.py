"""Parameter-only wrappers around the source-verified death/material exporters."""
import hashlib,json,math,struct
from pathlib import Path
import numpy as np
from enemy_fidelity_m2 import arr,Track,tr,qm

def track(b,p,fmt):
    interpolation,global_seq=struct.unpack_from('<Hh',b,p)
    return dict(interpolation=interpolation,globalSequence=global_seq,ranges=arr(b,p+4,'II'),times=[v[0] for v in arr(b,p+12,'I')],values=[v[0] if len(v)==1 else list(v) for v in arr(b,p+20,fmt)])
def sample(tr,seq,time,globals):
    if not tr['values']:return None
    start,end=0,len(tr['times'])-1
    if tr['globalSequence']>=0:
        length=globals[tr['globalSequence']];time=time%length if length else 0
    elif tr['ranges']:
        if seq>=len(tr['ranges']):return None
        start,end=tr['ranges'][seq]
    if start>end or start>=len(tr['times']):return None
    end=min(end,len(tr['times'])-1);times=tr['times'][start:end+1];values=tr['values'][start:end+1]
    index=int(np.searchsorted(times,time,side='right'))-1
    if index<0:return values[0]
    if index>=len(values)-1 or tr['interpolation']==0:return values[min(index,len(values)-1)]
    t=(time-times[index])/max(1,times[index+1]-times[index]);return (np.array(values[index])+(np.array(values[index+1])-values[index])*t).tolist()

def export_ghost_profiles(folder):
    b=(folder/'original.m2').read_bytes();model=json.loads((folder/'model.json').read_text())
    _,view=struct.unpack_from('<II',b,76);sub=arr(b,view+24,'10H3f');units=arr(b,view+32,'12H');materials=arr(b,132,'HH');translookup=arr(b,164,'H')
    colors_count,colors_offset=struct.unpack_from('<II',b,84);trans_count,trans_offset=struct.unpack_from('<II',b,100);globals=[x[0] for x in arr(b,20,'I')];anims=arr(b,28,'HHIIfIhHIII7fhH')
    checkpoints=[];offset=0
    for section in sub:checkpoints.append(offset);offset+=section[5]
    assert offset==len(model['triangles']) and len(units)==len(sub)
    passes=[]
    for unit in units:
        si=unit[2];ci=unit[4] if unit[4]<32768 else unit[4]-65536;ti=translookup[unit[10]][0]
        color=track(b,colors_offset+ci*56,'3f') if 0<=ci<colors_count else None
        alpha=track(b,colors_offset+ci*56+28,'h') if 0<=ci<colors_count else None
        weight=track(b,trans_offset+ti*28,'h') if 0<=ti<trans_count else None
        flags,blend=materials[unit[5]]
        passes.append(dict(sourceSubmesh=si,sourceMaterialIndex=unit[5],flags=flags,blend=blend,triangles=model['triangles'][checkpoints[si]:checkpoints[si]+sub[si][5]],colorTrack=color,alphaTrack=alpha,weightTrack=weight))
    clips={}
    for animation in model['animations']:
        seq=animation['sequenceIndex'];a=anims[seq];frames=[]
        for f in range(animation['frameCount']):
            time=a[2]+(a[3]-a[2])*f/(animation['frameCount']-1);colors=[]
            for p in passes:
                color=sample(p['colorTrack'],seq,time,globals) if p['colorTrack'] else [1,1,1]
                alpha=sample(p['alphaTrack'],seq,time,globals) if p['alphaTrack'] else 32767
                weight=sample(p['weightTrack'],seq,time,globals) if p['weightTrack'] else 32767
                colors.append([*(color or [1,1,1]),max(0,min(1,(32767 if alpha is None else alpha)/32767*(32767 if weight is None else weight)/32767))])
            frames.append(colors)
        clips[animation['name']]=frames
    groups=[];lookup={}
    for index,p in enumerate(passes):
        signature=json.dumps([p['flags'],p['blend'],p['colorTrack'],p['alphaTrack'],p['weightTrack']],sort_keys=True)
        if signature not in lookup:
            lookup[signature]=len(groups);groups.append(dict(flags=p['flags'],blend=p['blend'],triangles=[],sourcePassIndices=[],sourceSubmeshes=[]))
        group=groups[lookup[signature]];group['triangles'].extend(p['triangles']);group['sourcePassIndices'].append(index);group['sourceSubmeshes'].append(p['sourceSubmesh'])
    grouped_clips={key:[[colors[group['sourcePassIndices'][0]] for group in groups] for colors in frames] for key,frames in clips.items()}
    profile=dict(format='azeroth-ghost-material-v1',sourceSha256=hashlib.sha256(b).hexdigest(),sourceDisplayId=146,sourceMaterialFlagsEvidence='Original MD20v256 render flags and texture-unit table; only passes with identical flags/blend/complete source color tracks are batched',sourcePassCount=len(passes),passes=groups,clips=grouped_clips,sourceEmbeddedParticles=struct.unpack_from('<I',b,316)[0],sourceTexture=model['source']['texture'],nativePalettePolicy='Read actual native EnemyRenderer albedo/specular/rim property keys and its renderer property block; multiply source RGB by native albedo RGB; retain original source alpha')
    (folder/'ghost-material-profile.json').write_text(json.dumps(profile,separators=(',',':')))
    death=dict(next(c for c in model['animations'] if c['name']=='death'));a=anims[death['sequenceIndex']]
    death.update(sourceFrequency=a[6],sourceStartMs=a[2],sourceEndMs=a[3],sha256=hashlib.sha256((folder/death['file']).read_bytes()).hexdigest(),sourceSequenceSha256=hashlib.sha256(struct.pack('<HHIIfIhHIII7fhH',*a)).hexdigest())
    (folder/'death-profile.json').write_text(json.dumps(dict(format='azeroth-enemy-death-profile-v1',sourceModel=model['source']['model'],sourceSha256=profile['sourceSha256'],lifetimeSeconds=1.2,lifetimeEvidence='VA14593FBC6/FC43..FC5B',animations=[death]),indent=2))
    from PIL import Image
    image=Image.open(folder/'texture.png').convert('RGBA');(folder/'texture.rgba').write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes());(folder/'texture.rgba.json').write_text(json.dumps(dict(width=image.width,height=image.height,format='RGBA32',rowOrder='bottom-up')))
    return dict(SourcePasses=len(passes),BatchedRuntimePasses=len(groups),SourceParticles=profile['sourceEmbeddedParticles'],Clips=len(clips))

def export_death_profiles(archives, base_assets, output_assets):
    manifest=[]
    for folder in sorted(Path(base_assets).iterdir()):
        if folder.name not in {'boar','bonegolem','gnoll','infernal','kobold','lich','murloc','ogre','skeleton','tarantula','wolf'}:continue
        if not (folder/'model.json').exists():continue
        existing=json.loads((folder/'model.json').read_text());source,_=archives.read(existing['source']['model'])
        assert hashlib.sha256(source).hexdigest()==existing['source']['sha256']
        anims=arr(source,28,'HHIIfIhHIII7fhH');sequences=[(i,a) for i,a in enumerate(anims) if a[0]==1]
        if not sequences:raise ValueError('Missing original death '+folder.name)
        target=Path(output_assets)/folder.name;target.mkdir(parents=True,exist_ok=True)
        profile=dict(format='azeroth-enemy-death-profile-v1',sourceModel=existing['source']['model'],sourceSha256=existing['source']['sha256'],lifetimeSeconds=1.2,lifetimeEvidence='VA14593FBC6/FC43..FC5B',animations=[])
        for variant,(seq,a) in enumerate(sequences):
            label='death' if variant==0 else 'death_variant_'+str(seq)
            if variant==0:
                clip=dict(next(c for c in existing['animations'] if c['name']=='death'))
                assert clip['sequenceIndex']==seq
                framebytes=(folder/clip['file']).read_bytes()
            else:
                vertices=arr(source,68,'3f4B4B3f2f2f');pos=np.array([v[:3] for v in vertices]);weights=np.array([v[3:7] for v in vertices])/255.;bi=np.array([v[7:11] for v in vertices])
                if existing['source'].get('diffuseAtlas'):
                    _,viewoff=struct.unpack_from('<II',source,76);lookup=arr(source,viewoff,'H');indices=[lookup[t[0]][0] for t in arr(source,viewoff+8,'H')];submeshes=arr(source,viewoff+24,'10H3f');units=arr(source,viewoff+32,'12H');tl=arr(source,148,'H');primary={}
                    for unit in units:primary.setdefault(unit[2],tl[unit[8]][0])
                    seen={};source_indices=[]
                    for si,sub in enumerate(submeshes):
                        for vi in indices[sub[4]:sub[4]+sub[5]]:
                            key=(vi,primary[si])
                            if key not in seen:seen[key]=len(source_indices);source_indices.append(vi)
                    pos=pos[source_indices];weights=weights[source_indices];bi=bi[source_indices]
                assert len(pos)==existing['vertexCount']
                count,boneoff=struct.unpack_from('<II',source,52);bones=[]
                for i in range(count):
                    p=boneoff+i*108;bones.append(dict(parent=struct.unpack_from('<h',source,p+8)[0],pivot=np.array(struct.unpack_from('<3f',source,p+96)),t=Track(source,p+12,3,[0,0,0]),r=Track(source,p+40,4,[0,0,0,1],True),s=Track(source,p+68,3,[1,1,1])))
                globals=[v[0] for v in arr(source,20,'I')]
                def pose(time):
                    matrices={}
                    def world(i):
                        if i in matrices:return matrices[i]
                        bone=bones[i];pivot=bone['pivot'];m=tr(pivot)@tr(bone['t'].sample(time,seq,globals))@qm(bone['r'].sample(time,seq,globals))@np.diag([*bone['s'].sample(time,seq,globals),1])@tr(-pivot)
                        matrices[i]=world(bone['parent'])@m if bone['parent']>=0 else m;return matrices[i]
                    mats=np.array([world(i) for i in range(count)]);hom=np.concatenate([pos,np.ones((len(pos),1))],axis=1);v=np.sum(np.einsum('vbij,vj->vbi',mats[bi],hom)[:,:,:3]*weights[:,:,None],axis=1)
                    return np.stack([-v[:,1],v[:,2],v[:,0]],axis=1)
                duration=(a[3]-a[2])/1000;frames=max(2,int(math.ceil(duration*24))+1)
                poses=np.array([pose(a[2]+(a[3]-a[2])*i/(frames-1)) for i in range(frames)],dtype='<f4');framebytes=poses.tobytes()
                clip=dict(name=label,file=label+'.vertices.bin',animationId=1,sequenceIndex=seq,frameCount=frames,vertexCount=len(pos),duration=duration,fps=(frames-1)/duration,loop=False)
                (target/clip['file']).write_bytes(framebytes)
            clip.update(sourceFrequency=a[6],sourceStartMs=a[2],sourceEndMs=a[3],sha256=hashlib.sha256(framebytes).hexdigest(),sourceSequenceSha256=hashlib.sha256(struct.pack('<HHIIfIhHIII7fhH',*a)).hexdigest())
            profile['animations'].append(clip)
        (target/'death-profile.json').write_text(json.dumps(profile,indent=2))
        manifest.append(dict(key=folder.name,profile=profile))
    return manifest
