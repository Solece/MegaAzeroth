"""Independently compare private exported creatures with original MPQ records."""
import argparse, hashlib, json, struct
from pathlib import Path
import numpy as np
from enemy_fidelity_mpq import Archives
from enemy_fidelity_m2 import arr
from enemy_fidelity_blp import decode_blp
from PIL import Image

def sha(b):return hashlib.sha256(b).hexdigest()
def canonical(values):
    a=np.asarray(values,dtype=np.uint32).reshape(-1,3);a.sort(axis=1)
    a=a[np.lexsort((a[:,2],a[:,1],a[:,0]))]
    return sha(a.astype('<u4').tobytes())
def original_curve(b,p,fmt):
    mode,glob=struct.unpack_from('<Hh',b,p)
    return dict(interpolation=mode,globalSequence=glob,ranges=[list(v) for v in arr(b,p+4,'II')],times=[v[0] for v in arr(b,p+12,'I')],values=[list(v) for v in arr(b,p+20,fmt)])

def main():
    ap=argparse.ArgumentParser(__doc__);ap.add_argument('--data-dir',type=Path,required=True);ap.add_argument('--packs',type=Path,required=True);ap.add_argument('--report',type=Path,required=True);args=ap.parse_args()
    root=args.packs;manifest=json.loads((root/'enemy-variety-assets.json').read_text());checks=[]
    def check(model,name,value):
        checks.append(dict(model=model,check=name,passed=bool(value)))
        if not value:raise AssertionError(model+': '+name)
    check('all','eightDistinctOriginalModels',len(manifest['models'])==8 and len({m['sourceSha256']for m in manifest['models']})==8)
    with Archives(args.data_dir) as archive:
        for info in manifest['models']:
            key=info['key'];folder=root/key;model=json.loads((folder/'model.json').read_text());profile=json.loads((folder/'source-material-profile.json').read_text());death=json.loads((folder/'death-profile.json').read_text())
            b,arc=archive.read(info['sourcePath']);check(key,'originalModelSha',sha(b)==info['sourceSha256']==profile['sourceSha256']==model['source']['sha256'])
            check(key,'approvedDisplayId',info['displayId']==profile['sourceDisplayId']==model['source']['displayId'])
            check(key,'privateOriginalM2Unchanged',(folder/'original.m2').read_bytes()==b)
            vertices=arr(b,68,'3f4B4B3f2f2f');_,view=struct.unpack_from('<II',b,76);lookup=arr(b,view,'H');source_indices=[lookup[x[0]][0] for x in arr(b,view+8,'H')];subs=arr(b,view+24,'10H3f');units=arr(b,view+32,'12H');materials=arr(b,132,'HH');tex_lookup=arr(b,148,'H');coord_lookup=arr(b,156,'h');trans_lookup=arr(b,164,'H')
            sm=model['sourceVertexMap'];check(key,'appendOnlySourceVertexIdentity',sm[:len(vertices)]==list(range(len(vertices))) and sm==profile['sourceVertexMap'] and all(0<=i<len(vertices) for i in sm))
            check(key,'originalBaseGeometry',model['triangles']==np.asarray(source_indices).reshape(-1,3)[:,[0,2,1]].reshape(-1).tolist())
            expected_uv=np.asarray([vertices[i][14:16] for i in sm]);expected_uv[:,1]=1-expected_uv[:,1]
            check(key,'originalUnclampedUVs',np.array_equal(expected_uv,np.asarray(model['uv']).reshape(-1,2)))
            check(key,'allSourcePassesRetained',len(profile['passes'])==len(units)==info['sourcePassCount'])
            source_union=set()
            colors_n,colors_offset=struct.unpack_from('<II',b,84);alpha_n,alpha_offset=struct.unpack_from('<II',b,100)
            for pi,(p,u) in enumerate(zip(profile['passes'],units)):
                sub=subs[u[2]];original=source_indices[sub[4]:sub[4]+sub[5]];actual=[sm[v] for v in p['triangles']];source_union.update(actual);flags,blend=materials[u[5]]
                check(key,'pass'+str(pi)+'OriginalGeometry',len(actual)==len(original) and canonical(actual)==canonical(original)==p['sourceCanonicalTriangleSha256'])
                check(key,'pass'+str(pi)+'MaterialBinding',p['sourcePass']==pi and p['sourceSubmesh']==u[2] and p['sourceMaterialIndex']==u[5] and p['flags']==flags and p['blend']==blend and p['textureId']==tex_lookup[u[8]][0] and p['textureCoordMode']==coord_lookup[u[9]][0])
                ci=u[4] if u[4]<32768 else u[4]-65536;ai=trans_lookup[u[10]][0]
                expected_color=original_curve(b,colors_offset+ci*56,'3f') if 0<=ci<colors_n else None
                expected_alpha=original_curve(b,colors_offset+ci*56+28,'h') if 0<=ci<colors_n else None
                expected_weight=original_curve(b,alpha_offset+ai*28,'h') if 0<=ai<alpha_n else None
                check(key,'pass'+str(pi)+'OriginalVisibilityTracks',p['color']==expected_color and p['alpha']==expected_alpha and p['weight']==expected_weight)
            check(key,'allOriginalSectionsCovered',source_union==set(source_indices))
            for t in profile['textures']:
                tb,_=archive.read(t['sourcePath']);rgba=decode_blp(tb).convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
                check(key,'texture'+str(t['id'])+'OriginalPixelsAndHash',sha(tb)==t['sourceSha256'] and rgba==(folder/t['file']).read_bytes() and sha(rgba)==t['rgbaSha256'])
            original_anims=arr(b,28,'HHIIfIhHIII7fhH');check(key,'allRequiredAnimationIds',{0,1,4,5,16}<={a['animationId'] for a in model['animations']})
            for clip in model['animations']:
                source=original_anims[clip['sequenceIndex']];payload=(folder/clip['file']).read_bytes();frames=np.frombuffer(payload,dtype='<f4').reshape(clip['frameCount'],model['vertexCount'],3)
                check(key,clip['name']+'SourceSequenceAndFiniteFrames',source[0]==clip['animationId'] and source[2]==clip['sourceStartMs'] and source[3]==clip['sourceEndMs'] and abs(clip['duration']-(source[3]-source[2])/1000)<1e-8 and clip['frameCount']>=2 and np.isfinite(frames).all() and sha(payload)==clip['sha256'])
                check(key,clip['name']+'ActualMotion',np.max(np.abs(frames[-1]-frames[0]))>0 or np.max(np.abs(frames[len(frames)//2]-frames[0]))>0)
                if clip['name']=='idle':check(key,'bindPoseMatchesOriginalIdleFrame',np.allclose(frames[0],np.asarray(model['positions']).reshape(-1,3),atol=1e-6))
                if model['vertexCount']>len(vertices):check(key,clip['name']+'ReflectionDuplicatesExactSourcePositions',all(np.array_equal(frames[:,i],frames[:,sm[i]]) for i in range(len(vertices),len(sm))))
            source_deaths=[i for i,a in enumerate(original_anims) if a[0]==1]
            check(key,'allDeathVariantsAndFrequencies',source_deaths==[c['sequenceIndex'] for c in death['animations']] and all(c['sourceFrequency']==max(0,original_anims[c['sequenceIndex']][6]) for c in death['animations']))
            check(key,'billboardQuadsRetainOriginalVertices',len(profile['billboardQuads'])==info['sourceBillboardQuadCount'] and all(len(q['vertices'])==4 and all(0<=v<len(vertices) for v in q['vertices']) for q in profile['billboardQuads']))
            check(key,'allFileHashesMatchManifest',all(sha((folder/f['name']).read_bytes())==f['sha256'] for f in info['files']))
    report=dict(scope='Original MPQ to private pack validation only; native renderer/gameplay are not executed',passed=sum(c['passed']for c in checks),failed=sum(not c['passed']for c in checks),checks=checks)
    args.report.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({k:v for k,v in report.items()if k!='checks'},indent=2))

if __name__=='__main__':main()
