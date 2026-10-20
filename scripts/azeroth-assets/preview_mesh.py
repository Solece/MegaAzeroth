"""Re-read exported mesh and vertex frames; software texture rasterizer for QA."""
import json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
from mpq_source import ROOT

def render(folder,name='run',frame=0,yaw=40):
 d=json.loads((folder/'model.json').read_text());n=d['vertexCount'];anim=next(a for a in d['animations'] if a['name']==name);v=np.fromfile(folder/anim['file'],dtype='<f4').reshape(-1,n,3)[frame];uv=np.array(d['uv']).reshape(-1,2);tri=np.array(d['triangles']).reshape(-1,3);tex=np.array(Image.open(folder/d['texture']).convert('RGBA'))
 yaw=math.radians(yaw);view=np.array([[math.cos(yaw),0,-math.sin(yaw)],[math.sin(yaw)*.36,.933,math.cos(yaw)*.36],[math.sin(yaw)*.933,-.36,math.cos(yaw)*.933]])
 p=v@view.T;scale=400/max(np.ptp(p[:,0]),np.ptp(p[:,1]));p[:,:2]=(p[:,:2]-(p[:,:2].min(0)+p[:,:2].max(0))/2)*scale+[256,256];p[:,1]=512-p[:,1]
 canvas=np.full((512,512,3),[35,40,48],np.uint8);depth=np.full((512,512),-np.inf)
 for face in tri:
  a,b,c=p[face];den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
  if abs(den)<1e-8:continue
  low=np.maximum(np.floor(p[face,:2].min(0)).astype(int),0);high=np.minimum(np.ceil(p[face,:2].max(0)).astype(int),511)
  xx,yy=np.meshgrid(np.arange(low[0],high[0]+1),np.arange(low[1],high[1]+1));wa=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den;wb=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;wc=1-wa-wb;z=wa*a[2]+wb*b[2]+wc*c[2];mask=(wa>=0)&(wb>=0)&(wc>=0)&(z>depth[yy,xx])
  if not mask.any():continue
  u=wa*uv[face[0],0]+wb*uv[face[1],0]+wc*uv[face[2],0];vv=wa*uv[face[0],1]+wb*uv[face[1],1]+wc*uv[face[2],1];tx=np.minimum((u%1*tex.shape[1]).astype(int),tex.shape[1]-1);ty=np.minimum(((1-vv)%1*tex.shape[0]).astype(int),tex.shape[0]-1);normal=np.cross(v[face[1]]-v[face[0]],v[face[2]]-v[face[0]]);normal/=max(np.linalg.norm(normal),1e-8);shade=.65+.35*abs(np.dot(normal,np.array([.4,.8,-.3])));mask=mask&(tex[ty,tx,3]>=128);colors=np.uint8(np.clip(tex[ty,tx,:3]*shade,0,255));canvas[yy[mask],xx[mask]]=colors[mask];depth[yy[mask],xx[mask]]=z[mask]
 im=Image.fromarray(canvas);ImageDraw.Draw(im).text((16,15),f"{d['name']} {name} frame {frame}",fill='white');return im
if __name__=='__main__':
 for name in ['boar','murloc','skeleton','wolf','kobold','gnoll','tarantula']:
  folder=ROOT/'private-assets/azeroth'/name
  if not (folder/'model.json').exists():continue
  d=json.loads((folder/'model.json').read_text());run=next(a for a in d['animations'] if a['name']=='run');frames=[0,run['frameCount']//3,2*run['frameCount']//3];images=[render(folder,'run',f) for f in frames];sheet=Image.new('RGB',(1536,512))
  for i,im in enumerate(images):sheet.paste(im,(512*i,0))
  sheet.save(ROOT/f'reports/azeroth-assets/{name}-run-preview.png')



