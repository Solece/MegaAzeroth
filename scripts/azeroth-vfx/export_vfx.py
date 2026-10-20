"""Extract local Vanilla M2 spell assets; source files remain read-only."""
import json,struct,sys,hashlib,io,math,os
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0,r'D:/Games/Saitan/AzerothMegabonkLab/scripts/azeroth-assets')
from mpq_source import Archives
from convert_m2 import arr,Track,tr,qm
sys.path.insert(0,r'D:/Games/Saitan/AzerothMegabonkLab/scripts/azeroth-mage')
from blp_decode import decode_blp
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
work=Path(os.environ.get('AZEROTH_VFX_WORK',str(root/'private-assets/azeroth-vfx')))
out=work/'assets';out.mkdir(parents=True,exist_ok=True)
source=Archives();catalog=json.loads((work/'source-vfx.json').read_text())
def unity(v):return [-float(v[1]),float(v[2]),float(v[0])]
def track(b,p,fmt='f'):
 interp,glob=struct.unpack_from('<Hh',b,p)
 return dict(interpolation=interp,globalSequence=glob,ranges=arr(b,p+4,'II'),times=[v[0] for v in arr(b,p+12,'I')],values=[v[0] if len(v)==1 else list(v) for v in arr(b,p+20,fmt)])
def sample(t,time):
 vals=t['values'];times=t['times']
 if not vals:return 0
 i=int(np.searchsorted(times,time,side='right'))-1
 if i<0:return vals[0]
 if i>=len(vals)-1 or t['interpolation']==0:return vals[min(i,len(vals)-1)]
 a=np.array(vals[i]);b=np.array(vals[i+1]);return (a+(b-a)*(time-times[i])/max(1,times[i+1]-times[i])).tolist()
models={};textures={};failures=[]
for spell in catalog['spells']:
 for entry in spell['effects']:
  if not entry['available']:continue
  path=entry['source'];key=hashlib.sha256(path.lower().encode()).hexdigest()[:16];entry['key']=key
  if key in models:continue
  b,arc=source.read(path);n,poff=struct.unpack_from('<II',b,316)
  assert n==0 or poff+n*504<=len(b),(path,n,poff,len(b))
  ts=[]
  for ti,(typ,flags,length,off) in enumerate(arr(b,92,'4I')):
   p=b[off:off+length].rstrip(b'\0').decode('utf8') if typ==0 else ''
   if not p:ts.append(None);continue
   tk=hashlib.sha256(p.lower().encode()).hexdigest()[:16]
   try:
    if tk not in textures:
     data,ta=source.read(p);image=decode_blp(data);image.save(out/(tk+'.png'))
     (out/(tk+'.rgba')).write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
     textures[tk]=dict(key=tk,path=p,width=image.width,height=image.height,sha256=hashlib.sha256(data).hexdigest(),archive=ta)
    ts.append(tk)
   except Exception as e:ts.append(None);failures.append(dict(path=p,error=str(e)))
  emitters=[]
  for i in range(n):
   p=poff+i*504;pid,flags=struct.unpack_from('<II',b,p);bone,ti=struct.unpack_from('<HH',b,p+20)
   blend,shape=struct.unpack_from('<HH',b,p+40);ptype,tail,rotation,rows,cols=struct.unpack_from('<BBHHH',b,p+44)
   names=['speed','variation','vertical','horizontal','gravity','life','rate','length','width','zSource']
   tracks={name:track(b,p+52+j*28) for j,name in enumerate(names)}
   colors=struct.unpack_from('<12B',b,p+336);colors=[[colors[j+2]/255,colors[j+1]/255,colors[j]/255,colors[j+3]/255] for j in (0,4,8)]
   scales=struct.unpack_from('<3f',b,p+348);mid=struct.unpack_from('<f',b,p+332)[0]
   e=dict(texture=ts[ti] if ti<len(ts) else None,sourceTextureIndex=ti,bone=bone,flags=flags,blend=blend,shape=shape,particleType=ptype,tail=tail,rows=max(1,rows),cols=max(1,cols),position=unity(struct.unpack_from('<3f',b,p+8)),tracks=tracks,mid=mid,colors=colors,sizes=scales,tailLength=struct.unpack_from('<f',b,p+380)[0],drag=struct.unpack_from('<f',b,p+404)[0],spin=struct.unpack_from('<f',b,p+408)[0],enabled=track(b,p+476,'B'))
   if e['texture']:emitters.append(e)
  vs=arr(b,68,'3f4B4B3f2f2f');nodes=[];allAnims=arr(b,28,'HHIIfIhHIII7fhH');duration=max(.1,(allAnims[0][3]-allAnims[0][2])/1000) if allAnims else .8
  if vs:
   pos=np.array([v[:3] for v in vs]);weights=np.array([v[3:7] for v in vs])/255.;bi=np.array([v[7:11] for v in vs]);uv=np.array([v[14:16] for v in vs]);uv[:,1]=1-uv[:,1]
   globals=[x[0] for x in arr(b,20,'I')];anims=arr(b,28,'HHIIfIhHIII7fhH');seq=0
   a=anims[0] if anims else [0,0,0,800];start,end=a[2:4];duration=max(.1,(end-start)/1000)
   nBones,boneoff=struct.unpack_from('<II',b,52);bones=[]
   for j in range(nBones):
    p=boneoff+j*108;parent=struct.unpack_from('<h',b,p+8)[0];pivot=struct.unpack_from('<3f',b,p+96)
    bones.append(dict(parent=parent,pivot=pivot,t=Track(b,p+12,3,[0,0,0]),r=Track(b,p+40,4,[0,0,0,1],True),s=Track(b,p+68,3,[1,1,1])))
   def pose(time):
    mats={}
    def world(j):
     if j in mats:return mats[j]
     bn=bones[j];pv=np.array(bn['pivot']);m=tr(pv)@tr(bn['t'].sample(time,seq,globals))@qm(bn['r'].sample(time,seq,globals))@np.diag([*bn['s'].sample(time,seq,globals),1])@tr(-pv)
     mats[j]=world(bn['parent'])@m if bn['parent']>=0 else m;return mats[j]
    if not bones:return np.array([unity(v) for v in pos])
    allm=np.array([world(j) for j in range(nBones)]);hom=np.concatenate([pos,np.ones((len(pos),1))],axis=1)
    v=np.sum(np.einsum('vbij,vj->vbi',allm[bi],hom)[:,:,:3]*weights[:,:,None],axis=1)
    return np.array([unity(x) for x in v])
   frames=np.array([pose(start+(end-start)*j/max(1,int(duration*24))) for j in range(int(duration*24)+1)],dtype='<f4');frames.tofile(out/(key+'.vertices.bin'))
   _,voff=struct.unpack_from('<II',b,76);lookup=arr(b,voff,'H');tri=[lookup[x[0]][0] for x in arr(b,voff+8,'H')];sub=arr(b,voff+24,'10H3f');units=arr(b,voff+32,'12H');tl=arr(b,148,'H');materials=arr(b,132,'HH');translookup=arr(b,156,'H');colorsN,colorOff=struct.unpack_from('<II',b,84);transN,transOff=struct.unpack_from('<II',b,100)
   for unit in units:
    si=unit[2];ti=tl[unit[8]][0] if unit[8]<len(tl) else -1
    if si>=len(sub) or ti<0 or ti>=len(ts) or not ts[ti]:continue
    ci=unit[4];ci=ci if ci<32768 else ci-65536
    color=track(b,colorOff+ci*56,'3f') if 0<=ci<colorsN else None
    alpha=track(b,colorOff+ci*56+28,'h') if 0<=ci<colorsN else None
    transparency=translookup[unit[10]][0] if unit[10]<len(translookup) else -1
    weight=track(b,transOff+transparency*28,'h') if 0<=transparency<transN else None
    ss=sub[si];indices=np.array(tri[ss[4]:ss[4]+ss[5]]).reshape(-1,3)[:,[0,2,1]].reshape(-1).tolist()
    nodes.append(dict(texture=ts[ti],triangles=indices,blend=materials[unit[5]][1] if unit[5]<len(materials) else 2,color=color,alpha=alpha,weight=weight))
   geometry=dict(vertexCount=len(vs),positions=frames[0].reshape(-1).tolist(),uv=uv.reshape(-1).tolist(),frameCount=len(frames),duration=duration,file=key+'.vertices.bin',nodes=nodes)
  else:geometry=None
  ribbonCount,ribbonOffset=struct.unpack_from('<II',b,308);ribbons=[];materials=arr(b,132,'HH')
  for i in range(ribbonCount):
   rp=ribbonOffset+i*220;textureids=arr(b,rp+20,'H');materialids=arr(b,rp+28,'H');ti=textureids[0][0] if textureids else -1;mi=materialids[0][0] if materialids else -1
   if ti<0 or ti>=len(ts) or not ts[ti]:continue
   ribbons.append(dict(texture=ts[ti],blend=materials[mi][1] if 0<=mi<len(materials) else 4,position=unity(struct.unpack_from('<3f',b,rp+8)),color=track(b,rp+36,'3f'),alpha=track(b,rp+64,'h'),above=track(b,rp+92),below=track(b,rp+120),edgesPerSecond=struct.unpack_from('<f',b,rp+148)[0],life=struct.unpack_from('<f',b,rp+152)[0],visible=track(b,rp+192,'B')))
  model=dict(key=key,source=path,sha256=entry['sha256'],archive=arc,textures=ts,geometry=geometry,emitters=emitters,duration=duration,startMs=allAnims[0][2] if allAnims else 0,globals=[v[0] for v in arr(b,20,'I')],ribbons=ribbons,ribbonCount=ribbonCount)
  (out/(key+'.json')).write_text(json.dumps(model,separators=(',',':')));models[key]=model
manifest=dict(format='azeroth-spell-vfx-v1',sources=['https://github.com/wowdev/WoWDBDefs','https://github.com/samwhosung/wow-1121-client-internals/blob/main/docs/models.md','https://github.com/wowdev/pywowlib/blob/master/file_formats/m2_format.py'],spells=[dict(id=s['id'],spellId=s['spellId'],visualId=s['visualId'],effects=[dict(stage=e['stage'],slot=e.get('slot'),key=e['key'],path=e['source']) for e in s['effects'] if 'key' in e]) for s in catalog['spells']],textures=list(textures.values()),limitations=['Native particle/ribbon simulation is adapted to Unity meshes; original textures, emitter settings and mesh animation retained.','Recursive model particles and specialized source blend details are not yet rendered.'])
(out/'manifest.json').write_text(json.dumps(manifest,indent=2))
print(json.dumps(dict(models=len(models),textures=len(textures),emitters=sum(len(m['emitters']) for m in models.values()),meshNodes=sum(len(m['geometry']['nodes']) if m['geometry'] else 0 for m in models.values()),failures=failures),indent=2))
