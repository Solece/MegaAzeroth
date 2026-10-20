"""Build a local human male Mage from the installed Azeroth MPQs.

DBC schemas: wowdev/WoWDBDefs, Vanilla 1.12 layouts. No game data is distributed.
"""
import hashlib, io, json, math, struct, sys
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
sys.path.insert(0,str(ROOT/'scripts/azeroth-assets'))
from mpq_source import Archives
from convert_m2 import arr, Track, tr, qm, flat
source=Archives()
OUT=ROOT/'private-assets/azeroth-mage/human-male'
OUT.mkdir(parents=True,exist_ok=True)
evidence=[]
def read(path):
 b,arc=source.read(path); evidence.append(dict(path=path,archive=arc,sha256=hashlib.sha256(b).hexdigest()));return b
def image(path):
 b=read(path)
 if b[:4]==b'BLP2' and b[8]==1:
  # Pillow currently treats palette alpha as pixel alpha and ignores the
  # separate paletted-BLP2 alpha plane. Decode that format explicitly.
  w,h=struct.unpack_from('<II',b,12);off=struct.unpack_from('<I',b,20)[0];count=w*h
  pal=np.frombuffer(b[148:1172],dtype=np.uint8).reshape(256,4);ix=np.frombuffer(b[off:off+count],dtype=np.uint8);px=pal[ix][:,[2,1,0,3]].copy();depth=b[9];alpha=b[off+count:]
  if depth==0:px[:,3]=255
  elif depth==8:px[:,3]=np.frombuffer(alpha[:count],dtype=np.uint8)
  elif depth in [1,4]:
   bits=np.frombuffer(alpha[:math.ceil(count*depth/8)],dtype=np.uint8);sh=(np.arange(count)*depth)%8;px[:,3]=((bits[np.arange(count)*depth//8]>>sh)&((1<<depth)-1))*255//((1<<depth)-1)
  else:raise ValueError('Unsupported palette alpha depth '+str(depth))
  return Image.fromarray(px.reshape(h,w,4))
 return Image.open(io.BytesIO(b)).convert('RGBA')
def dbc(name):
 b=read('DBFilesClient\\'+name+'.dbc');n,nf,stride,ss=struct.unpack_from('<4I',b,4);s=b[20+n*stride:]
 def st(off):return s[off:s.find(b'\0',off)].decode('utf8','replace')
 return [struct.unpack_from('<'+'I'*(stride//4),b,20+i*stride) for i in range(n)],st
sections,ss=dbc('CharSections');outfits,os=dbc('CharStartOutfit');items,its=dbc('ItemDisplayInfo')
outfit=next(r for r in outfits if (r[1]&0xffffff)==0x000801)
itemrows={r[0]:r for r in items}
regions=[(0,0,128,64),(0,64,128,64),(0,128,128,32),(128,0,128,64),(128,64,128,32),(128,96,128,64),(128,160,128,64),(128,224,128,32)]
regiondirs=['ArmUpperTexture','ArmLowerTexture','HandTexture','TorsoUpperTexture','TorsoLowerTexture','LegUpperTexture','LegLowerTexture','FootTexture']
def section(typ,var=0,color=0):return next(r for r in sections if r[1:6]==(1,0,typ,var,color) and r[9]==0)
body=image(ss(section(0)[6])); assert body.size==(256,256)
def overlay(path,rect):
 im=image(path);x,y,w,h=rect;im=im.resize((w,h),Image.Resampling.BILINEAR);body.alpha_composite(im,(x,y))
face=section(1);overlay(ss(face[6]),(0,192,128,64));overlay(ss(face[7]),(0,160,128,32))
under=section(4)
if under[6]:overlay(ss(under[6]),(128,96,128,64))
# Dress in original CharStartOutfit order by inventory slot: shirt, trousers,
# boots, chest/robe. Exclude weapons and consumables from the body composition.
equipment=[]
for j in sorted(range(12),key=lambda j:{4:0,7:1,8:2,5:3,20:3}.get(outfit[26+j],-1)):
 itemid,display,slot=outfit[2+j],outfit[14+j],outfit[26+j]
 if slot not in [4,7,8,5,20] or display not in itemrows:continue
 row=itemrows[display];textures=[]
 for k,off in enumerate(row[14:22]):
  if not off:continue
  stem=its(off);found=None
  for suffix in ['_M','_U','']:
   path='Item\\TextureComponents\\'+regiondirs[k]+'\\'+stem+suffix+'.blp'
   try:overlay(path,regions[k]);found=path;break
   except FileNotFoundError:pass
  if found is None:raise FileNotFoundError(stem)
  textures.append(found)
 equipment.append(dict(itemId=itemid,displayId=display,inventoryType=slot,geosetGroup=list(row[6:9]),textures=textures))
body.save(OUT/'texture.png')
(OUT/'texture.rgba').write_bytes(body.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
(OUT/'texture.rgba.json').write_text(json.dumps(dict(width=256,height=256,format='RGBA32',rowOrder='bottom-up')))
p='Character\\Human\\Male\\HumanMale.m2';b=read(p);assert b[:4]==b'MD20' and struct.unpack_from('<I',b,4)[0]==256
vs=arr(b,68,'3f4B4B3f2f2f');allpos=np.array([v[:3] for v in vs]);allweights=np.array([v[3:7] for v in vs])/255.;allbi=np.array([v[7:11] for v in vs]);allnorm=np.array([v[11:14] for v in vs]);alluv=np.array([v[14:16] for v in vs]);alluv[:,1]=1-alluv[:,1]
vo=struct.unpack_from('<I',b,80)[0];lookup=arr(b,vo,'H');rawtri=arr(b,vo+8,'H');indices=[lookup[t[0]][0] for t in rawtri];submeshes=arr(b,vo+24,'10H3f')
# Bald, clean-shaven, source appearance 0; robe sets sleeves 802 and skirt
# 1302. Keep source base-body/facial/glove geosets, hide cloak and boot volume
# under the robe. No arbitrary NPC display model is substituted.
selected_ids={0,101,201,301,401,802,1302}
selected=[s for s in submeshes if s[0] in selected_ids]
used=[];remap={};newindices=[]
for sub in selected:
 for vi in indices[sub[4]:sub[4]+sub[5]]:
  if vi not in remap:remap[vi]=len(used);used.append(vi)
  newindices.append(remap[vi])
pos=allpos[used];weights=allweights[used];bi=allbi[used];normals=allnorm[used];uv=alluv[used]
nBones,boneoff=struct.unpack_from('<II',b,52);bones=[]
for i in range(nBones):
 off=boneoff+i*108;key,flags,parent,submesh=struct.unpack_from('<iIhH',b,off);pivot=struct.unpack_from('<3f',b,off+96)
 bones.append(dict(parent=parent,flags=flags,pivot=pivot,t=Track(b,off+12,3,[0,0,0]),r=Track(b,off+40,4,[0,0,0,1],True),s=Track(b,off+68,3,[1,1,1])))
globals=[x[0] for x in arr(b,20,'I')];anims=arr(b,28,'HHIIfIhHIII7fhH')
def unity(x):return np.stack([-x[:,1],x[:,2],x[:,0]],axis=1)
def matrices(time,seq):
 mats={}
 def world(i):
  if i in mats:return mats[i]
  bone=bones[i];pv=bone['pivot'];s=bone['s'].sample(time,seq,globals);local=tr(pv)@tr(bone['t'].sample(time,seq,globals))@qm(bone['r'].sample(time,seq,globals))@np.diag([*s,1.])@tr(-np.array(pv))
  mats[i]=world(bone['parent'])@local if bone['parent']>=0 else local;return mats[i]
 return np.array([world(i) for i in range(nBones)])
def pose(time,seq):
 allm=matrices(time,seq);hom=np.concatenate([pos,np.ones((len(pos),1))],axis=1);transformed=np.einsum('vbij,vj->vbi',allm[bi],hom)
 return unity(np.sum(transformed[:,:,:3]*weights[:,:,None],axis=1))
attachment_count,attachment_offset=struct.unpack_from('<II',b,260)
attachment_rows=[struct.unpack_from('<IHH3f',b,attachment_offset+i*48) for i in range(attachment_count)]
attachment_map={r[0]:r for r in attachment_rows}
hand_ids=[21 if 21 in attachment_map else 2,22 if 22 in attachment_map else 1]
def hands(time,seq):
 m=matrices(time,seq);points=[]
 for ident in hand_ids:
  r=attachment_map[ident];points.append((m[r[1]]@np.array([*r[3:6],1]))[:3])
 return unity(np.array(points))
clips=[];fps=24;mn=np.full(3,np.inf);mx=np.full(3,-np.inf)
for label,animid in [('idle',0),('walk',4),('run',5),('jump_start',37),('jump',38),('jump_end',39),('ready_spell',51),('ready_omni',52),('cast',53),('cast_omni',54),('channel',124),('death',1)]:
 found=next(((i,a) for i,a in enumerate(anims) if a[0]==animid),None)
 if found is None:continue
 seq,a=found;start,end=a[2:4];duration=(end-start)/1000;fc=max(2,int(math.ceil(duration*fps))+1);frames=np.array([pose(start+(end-start)*i/(fc-1),seq) for i in range(fc)],dtype='<f4');assert np.isfinite(frames).all();file=label+'.vertices.bin';frames.tofile(OUT/file);mn=np.minimum(mn,frames.min(axis=(0,1)));mx=np.maximum(mx,frames.max(axis=(0,1)))
 attachment_frames=np.array([hands(start+(end-start)*i/(fc-1),seq) for i in range(fc)],dtype='<f4');attachment_file=label+'.attachments.bin';attachment_frames.tofile(OUT/attachment_file)
 clips.append(dict(name=label,file=file,animationId=animid,sequenceIndex=seq,frameCount=fc,vertexCount=len(used),duration=duration,fps=(fc-1)/duration,loop=label in ['idle','walk','run','channel'],attachmentsFile=attachment_file,attachmentIds=hand_ids,attachmentCount=2,maxVertexDisplacement=float(np.max(np.linalg.norm(frames-frames[0],axis=2)))))
 print(label,fc,duration,flush=True)
idle=next(i for i,a in enumerate(anims) if a[0]==0);base=pose(anims[idle][2],idle);tri=np.array(newindices).reshape(-1,3)[:,[0,2,1]]
d=dict(format='azeroth-unity-mesh-v1',name='HumanMaleMage',coordinateSystem='Unity left-handed Y-up +Z-forward: source (x,y,z) -> (-y,z,x); V flipped',texture='texture.png',vertexCount=len(used),positions=flat(base),normals=flat(unity(normals)),uv=flat(uv),triangles=flat(tri),animations=clips,boundsMin=mn.tolist(),boundsMax=mx.tolist(),source=dict(model=p,version=256,bodySha256=hashlib.sha256(b).hexdigest(),raceId=1,classId=8,sexId=0,skinColor=0,faceStyle=0,hairStyle=0,facialHairStyle=0,outfitId=outfit[0],selectedGeosetIds=sorted(selected_ids),equipment=equipment,boneCount=nBones,evidence=evidence,limitation='Verified original human male Mage starter outfit; no claim to reproduce an unidentified last-selected Azeroth save appearance.'))
(OUT/'model.json').write_text(json.dumps(d,separators=(',',':')))
(OUT/'source-evidence.json').write_text(json.dumps(d['source'],indent=2))
print(json.dumps(dict(output=str(OUT),vertices=len(used),triangles=len(tri),clips=len(clips),equipment=equipment),indent=2))
