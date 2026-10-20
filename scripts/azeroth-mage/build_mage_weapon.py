"""Export the installed Human Mage starter weapon on its original hand attachment.

Separate visual pack: never changes the 810-vertex Mage body or combat ownership.
"""
from pathlib import Path
import hashlib,json,math,struct,sys
import numpy as np
from PIL import Image
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
sys.path.insert(0,str(ROOT/'scripts/azeroth-assets'))
sys.path.insert(0,str(ROOT/'scripts/azeroth-mage'))
from mpq_source import Archives
from convert_m2 import arr,Track,tr,qm,flat
from blp_decode import decode_blp
OUT=ROOT/'private-assets/azeroth-mage/human-male-weapon';OUT.mkdir(parents=True,exist_ok=True)
archive=Archives();evidence=[]
def read(path):
 data,mpq=archive.read(path);evidence.append(dict(path=path,archive=mpq,sha256=hashlib.sha256(data).hexdigest()));return data
def dbc(name):
 b=read('DBFilesClient\\'+name+'.dbc');n,nf,stride,ss=struct.unpack_from('<4I',b,4);strings=b[20+n*stride:]
 def text(off):return strings[off:strings.find(b'\0',off)].decode('utf8','replace')
 return [struct.unpack_from('<'+'I'*(stride//4),b,20+i*stride) for i in range(n)],text
outfits,_=dbc('CharStartOutfit');displays,displaytext=dbc('ItemDisplayInfo')
outfit=next(r for r in outfits if (r[1]&0xffffff)==0x000801)
slot=next(i for i in range(12) if outfit[26+i] in [13,17,21])
itemid,displayid,inventorytype=outfit[2+slot],outfit[14+slot],outfit[26+slot]
display=next(r for r in displays if r[0]==displayid)
modelpath='Item\\ObjectComponents\\Weapon\\'+displaytext(display[1]).replace('.mdx','.m2')
texturepath='Item\\ObjectComponents\\Weapon\\'+displaytext(display[3])+'.blp'
weapon=read(modelpath);assert weapon[:4]==b'MD20' and struct.unpack_from('<I',weapon,4)[0]==256
vertices=arr(weapon,68,'3f4B4B3f2f2f');positions=np.array([v[:3] for v in vertices]);normals=np.array([v[11:14] for v in vertices]);uv=np.array([v[14:16] for v in vertices]);uv[:,1]=1-uv[:,1]
assert all(x[0] in [0,2] for x in arr(weapon,92,'4I'))
view=struct.unpack_from('<I',weapon,80)[0];lookup=arr(weapon,view,'H');tri=np.array([lookup[t[0]][0] for t in arr(weapon,view+8,'H')]).reshape(-1,3)[:,[0,2,1]]
texture=decode_blp(read(texturepath));texture.save(OUT/'texture.png');(OUT/'texture.rgba').write_bytes(texture.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes());(OUT/'texture.rgba.json').write_text(json.dumps(dict(width=texture.width,height=texture.height,format='RGBA32',rowOrder='bottom-up')))
humanpath='Character\\Human\\Male\\HumanMale.m2';human=read(humanpath);bonecount,boneoff=struct.unpack_from('<II',human,52);bones=[]
for i in range(bonecount):
 off=boneoff+i*108;_,flags,parent,_=struct.unpack_from('<iIhH',human,off)
 bones.append(dict(parent=parent,pivot=struct.unpack_from('<3f',human,off+96),t=Track(human,off+12,3,[0,0,0]),r=Track(human,off+40,4,[0,0,0,1],True),s=Track(human,off+68,3,[1,1,1])))
globals=[x[0] for x in arr(human,20,'I')];anims=arr(human,28,'HHIIfIhHIII7fhH')
n,off=struct.unpack_from('<II',human,260);attachment=next(struct.unpack_from('<IHH3f',human,off+i*48) for i in range(n) if struct.unpack_from('<I',human,off+i*48)[0]==1)
def unity(v):return np.stack([-v[:,1],v[:,2],v[:,0]],axis=1)
def matrix(time,seq):
 cache={}
 def world(i):
  if i in cache:return cache[i]
  b=bones[i];p=np.array(b['pivot']);local=tr(p)@tr(b['t'].sample(time,seq,globals))@qm(b['r'].sample(time,seq,globals))@np.diag([*b['s'].sample(time,seq,globals),1])@tr(-p)
  cache[i]=world(b['parent'])@local if b['parent']>=0 else local;return cache[i]
 return world(attachment[1])@tr(attachment[3:6])
body=json.loads((ROOT/'private-assets/azeroth-mage/human-male/model.json').read_text())
clips=[];first=None
hom=np.concatenate([positions,np.ones((len(positions),1))],axis=1)
for clip in body['animations']:
 seq=clip['sequenceIndex'];start,end=anims[seq][2:4];count=clip['frameCount'];frames=[];grips=[]
 for f in range(count):
  m=matrix(start+(end-start)*f/(count-1),seq);frames.append(unity((hom@m.T)[:,:3]));grips.append(unity(m[:3,3][None,:])[0])
 frames=np.asarray(frames,dtype='<f4');grips=np.asarray(grips,dtype='<f4');assert np.isfinite(frames).all() and np.isfinite(grips).all()
 file=clip['name']+'.vertices.bin';frames.tofile(OUT/file);grips.tofile(OUT/(clip['name']+'.grip.bin'))
 if first is None:first=frames[0]
 clips.append(dict(name=clip['name'],file=file,animationId=clip['animationId'],sequenceIndex=seq,frameCount=count,vertexCount=len(vertices),duration=clip['duration'],loop=clip['loop'],gripFile=clip['name']+'.grip.bin'))
source=dict(itemId=itemid,displayId=displayid,inventoryType=inventorytype,outfitId=outfit[0],model=modelpath,texture=texturepath,humanModel=humanpath,attachmentId=attachment[0],attachmentBone=attachment[1],attachmentPosition=attachment[3:6],attachmentMethod='original bone animation matrix * original attachment translation; source weapon geometry, no invented rotation/scale',evidence=evidence)
model=dict(format='azeroth-unity-mesh-v1',name='HumanMaleMageStarterStaff',texture='texture.png',vertexCount=len(vertices),positions=flat(first),normals=flat(unity(normals)),uv=flat(uv),triangles=flat(tri),animations=clips,source=source)
(OUT/'model.json').write_text(json.dumps(model,separators=(',',':')));(OUT/'source-evidence.json').write_text(json.dumps(source,indent=2))
report=dict(status='PASS',itemId=itemid,displayId=displayid,vertexCount=len(vertices),triangles=len(tri),clips=len(clips),frames=sum(c['frameCount'] for c in clips),attachmentId=1,bone=attachment[1],textureSize=texture.size,weaponBoundsMin=positions.min(0).tolist(),weaponBoundsMax=positions.max(0).tolist(),sourceEvidence=source)
(OUT/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
