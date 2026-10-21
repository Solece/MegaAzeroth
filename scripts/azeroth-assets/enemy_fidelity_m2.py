"""Local Vanilla M2 reader. Asset data stays private; source game is read-only.
References: wowdev/pywowlib m2_format.py and wow-1121-client-internals/models.md.
"""
import struct,json,math,hashlib,io,sys
from pathlib import Path
from enemy_fidelity_blp import decode_blp
import numpy as np
from PIL import Image

def arr(b,p,fmt):
 n,off=struct.unpack_from('<II',b,p);sz=struct.calcsize('<'+fmt)
 assert not n or off+n*sz<=len(b),(p,n,off,sz)
 return [struct.unpack_from('<'+fmt,b,off+i*sz) for i in range(n)]
def flat(x):return np.asarray(x).reshape(-1).tolist()
def tr(v):
 m=np.eye(4);m[:3,3]=v;return m
def qm(q):
 q=np.asarray(q,dtype=float);n=np.linalg.norm(q)
 if n<1e-8:return np.eye(4)
 x,y,z,w=q/n
 return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w),0],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w),0],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y),0],[0,0,0,1.]])
def slerp(a,b,t):
 a=np.array(a,dtype=float);b=np.array(b,dtype=float);a/=np.linalg.norm(a);b/=np.linalg.norm(b);d=np.dot(a,b)
 if d<0:b=-b;d=-d
 if d>.9995:return a+t*(b-a)
 theta=math.acos(min(1,max(-1,d)));return (math.sin((1-t)*theta)*a+math.sin(t*theta)*b)/math.sin(theta)
class Track:
 def __init__(self,b,p,dims,default,rot=False):
  self.interp,self.glob=struct.unpack_from('<Hh',b,p);self.ranges=arr(b,p+4,'II');self.times=np.asarray(arr(b,p+12,'I')).reshape(-1);self.values=np.asarray(arr(b,p+20,'f'*dims));self.default=default;self.rot=rot
  assert len(self.times)==len(self.values)
  assert self.interp in [0,1],self.interp
 def sample(self,t,seq,globals):
  if not len(self.values):return self.default
  start,end=0,len(self.times)-1
  if self.glob>=0:t=t%globals[self.glob] if globals[self.glob]>0 else 0
  elif self.ranges:
   if seq>=len(self.ranges):return self.default
   start,end=self.ranges[seq]
  if start>end or start>=len(self.times):return self.default
  end=min(end,len(self.times)-1);j=int(np.searchsorted(self.times[start:end+1],t,side='right'))+start
  if j<=start:return self.values[start]
  if j>end:return self.values[end]
  i=j-1;d=self.times[j]-self.times[i];f=(t-self.times[i])/d if d else 0
  if self.interp==0:return self.values[i]
  return slerp(self.values[i],self.values[j],f) if self.rot else self.values[i]*(1-f)+self.values[j]*f
def convert(source,name,skin,display_id,model_path=None,skin_folder=None,output_dir=None):
 if output_dir is None:raise ValueError('An explicit output_dir is required')
 p=model_path or f'Creature\\{name}\\{name}.m2';b,archive=source.read(p);out=Path(output_dir);out.mkdir(parents=True,exist_ok=True);skin_folder=skin_folder or f'Creature\\{name}'
 assert b[:4]==b'MD20' and struct.unpack_from('<I',b,4)[0]==256
 vs=arr(b,68,'3f4B4B3f2f2f');pos=np.array([v[:3] for v in vs]);weights=np.array([v[3:7] for v in vs])/255.;bi=np.array([v[7:11] for v in vs]);normals=np.array([v[11:14] for v in vs]);uv=np.array([v[14:16] for v in vs]);uv[:,1]=1-uv[:,1]
 nviews,viewoff=struct.unpack_from('<II',b,76);lookup=arr(b,viewoff,'H');rawtri=arr(b,viewoff+8,'H');indices=[lookup[t[0]][0] for t in rawtri]
 submeshes=arr(b,viewoff+24,'10H3f');texunits=arr(b,viewoff+32,'12H')
 nBones,boneoff=struct.unpack_from('<II',b,52);bones=[]
 for i in range(nBones):
  off=boneoff+i*108;key,flags,parent,submesh=struct.unpack_from('<iIhH',b,off);pivot=struct.unpack_from('<3f',b,off+96)
  bones.append(dict(parent=parent,flags=flags,pivot=pivot,t=Track(b,off+12,3,[0,0,0]),r=Track(b,off+40,4,[0,0,0,1],True),s=Track(b,off+68,3,[1,1,1])))
 assert max(bi.flatten())<nBones
 globals=[x[0] for x in arr(b,20,'I')];anims=arr(b,28,'HHIIfIhHIII7fhH');texturemeta=arr(b,92,'4I')
 skins=[skin] if isinstance(skin,str) else skin
 texpath=f'{skin_folder}\\{skins[0]}.blp';tex,texarc=source.read(texpath);image=decode_blp(tex);texture_sources=[texpath]
 # Use the first (diffuse) pass of each submesh. Reflection/additive passes are omitted.
 texlookup=arr(b,148,'H');submesh_texture={}
 for unit in texunits:
  submesh_texture.setdefault(unit[2],texlookup[unit[8]][0])
 primary_ids=sorted(set(submesh_texture.values()))
 if len(primary_ids)>1:
  images={}
  for ti in primary_ids:
   typ,flags,length,off=texturemeta[ti]
   if typ in [11,12,13]:path=f'{skin_folder}\\{skins[typ-11]}.blp'
   elif typ==0:path=b[off:off+length].rstrip(b'\0').decode('utf8')
   else:raise ValueError(f'Unsupported primary texture replacement {typ}')
   tb,_=source.read(path);images[ti]=decode_blp(tb);(out/f'original-texture-{ti}.blp').write_bytes(tb);texture_sources.append(path)
  # Four-pixel duplicated gutters avoid bilinear bleeding between atlas cells.
  pad=4;w=sum(im.width+2*pad for im in images.values());h=max(im.height+2*pad for im in images.values());atlas=Image.new('RGBA',(w,h));rects={};x=0
  for ti,im in images.items():
   atlas.paste(im,(x+pad,pad));rects[ti]=(x+pad,pad,im.width,im.height)
   atlas.paste(im.crop((0,0,1,im.height)).resize((pad,im.height)),(x,pad));atlas.paste(im.crop((im.width-1,0,im.width,im.height)).resize((pad,im.height)),(x+pad+im.width,pad));atlas.paste(im.crop((0,0,im.width,1)).resize((im.width,pad)),(x+pad,0));atlas.paste(im.crop((0,im.height-1,im.width,im.height)).resize((im.width,pad)),(x+pad,pad+im.height));x+=im.width+2*pad
  remap={};source_indices=[];outuv=[];newindices=[]
  for si,sub in enumerate(submeshes):
   ti=submesh_texture[si];rx,ry,rw,rh=rects[ti]
   for vi in indices[sub[4]:sub[4]+sub[5]]:
    key=(vi,ti)
    if key not in remap:
     remap[key]=len(source_indices);source_indices.append(vi);u,v=np.clip(uv[vi],0,1);outuv.append(((rx+u*rw)/w,1-(ry+(1-v)*rh)/h))
    newindices.append(remap[key])
  pos=pos[source_indices];weights=weights[source_indices];bi=bi[source_indices];normals=normals[source_indices];uv=np.asarray(outuv);vs=[vs[i] for i in source_indices];indices=newindices;image=atlas
 image.save(out/'texture.png')
 (out/'texture.rgba').write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
 (out/'texture.rgba.json').write_text(json.dumps(dict(width=image.width,height=image.height,format='RGBA32',origin='bottom-left')))
 (out/'original.m2').write_bytes(b);(out/'original.blp').write_bytes(tex)
 def unity(x):return np.stack([-x[:,1],x[:,2],x[:,0]],axis=1)
 def pose(time,seq):
  mats={}
  def world(i):
   if i in mats:return mats[i]
   bone=bones[i];pivot=bone['pivot'];s=bone['s'].sample(time,seq,globals);local=tr(pivot)@tr(bone['t'].sample(time,seq,globals))@qm(bone['r'].sample(time,seq,globals))@np.diag([*s,1.])@tr(-np.array(pivot))
   mats[i]=world(bone['parent'])@local if bone['parent']>=0 else local;return mats[i]
  allm=np.array([world(i) for i in range(nBones)]);hom=np.concatenate([pos,np.ones((len(pos),1))],axis=1);transformed=np.einsum('vbij,vj->vbi',allm[bi],hom)
  return unity(np.sum(transformed[:,:,:3]*weights[:,:,None],axis=1))
 animation_out=[];fps=24;union_min=np.full(3,np.inf);union_max=np.full(3,-np.inf)
 for label,animid in [('idle',0),('walk',4),('run',5),('attack',16),('death',1),('cast',53),('cast_omni',54),('channel',124),('cast_basic',32)]:
  found=next(((i,a) for i,a in enumerate(anims) if a[0]==animid),None)
  if found is None:continue
  seq,a=found;start,end=a[2:4];duration=max(1/24,(end-start)/1000);fc=max(2,int(math.ceil(duration*fps))+1);frames=np.array([pose(start+(end-start)*i/(fc-1),seq) for i in range(fc)],dtype='<f4');file=label+'.vertices.bin';frames.tofile(out/file)
  union_min=np.minimum(union_min,frames.min(axis=(0,1)));union_max=np.maximum(union_max,frames.max(axis=(0,1)))
  animation_out.append(dict(name=label,file=file,animationId=animid,sequenceIndex=seq,frameCount=fc,vertexCount=len(vs),duration=duration,fps=(fc-1)/duration,loop=label in ['idle','walk','run','channel'],maxVertexDisplacement=float(np.max(np.linalg.norm(frames-frames[0],axis=2)))))
 idle_seq=next(i for i,a in enumerate(anims) if a[0]==0);base=pose(anims[idle_seq][2],idle_seq);tri=np.array(indices).reshape(-1,3)[:,[0,2,1]]
 result=dict(format='azeroth-unity-mesh-v1',name=name,coordinateSystem='Unity left-handed Y-up +Z-forward: source (x,y,z) -> (-y,z,x); V flipped',texture='texture.png',vertexCount=len(vs),positions=flat(base),normals=flat(unity(normals)),uv=flat(uv),triangles=flat(tri),animations=animation_out,source=dict(model=p,archive=archive,sha256=hashlib.sha256(b).hexdigest(),version=256,displayId=display_id,skin=skin,texture=texpath,textureArchive=texarc,textureMagic=tex[:4].decode(),boneCount=nBones,submeshCount=len(submeshes),materialCount=len(texturemeta)))
 result['boundsMin']=np.minimum(union_min,base.min(0)).tolist();result['boundsMax']=np.maximum(union_max,base.max(0)).tolist();result['source']['textureSources']=list(dict.fromkeys(texture_sources));result['source']['diffuseAtlas']=len(primary_ids)>1
 (out/'model.json').write_text(json.dumps(result,separators=(',',':')))
 with (out/'preview.obj').open('w') as f:
  f.write('mtllib preview.mtl\nusemtl body\n')
  for v in base:f.write('v '+' '.join(map(str,v))+'\n')
  for v in uv:f.write('vt '+' '.join(map(str,v))+'\n')
  for face in tri+1:f.write('f '+' '.join(f'{i}/{i}' for i in face)+'\n')
 (out/'preview.mtl').write_text('newmtl body\nKd 1 1 1\nmap_Kd texture.png\n')
 summary=dict(name=name,output=str(out),vertices=len(vs),triangles=len(tri),bones=nBones,texture=Image.open(out/'texture.png').size,submeshes=submeshes,textureUnits=texunits,textureMetadata=texturemeta,animations=animation_out)
 (out/'conversion.json').write_text(json.dumps(summary,indent=2));print(json.dumps(dict(name=name,displayId=display_id,output=str(out),vertices=len(vs),triangles=len(tri),bones=nBones,animations=[dict(name=a['name'],animationId=a['animationId'],frames=a['frameCount']) for a in animation_out])),flush=True);return out
