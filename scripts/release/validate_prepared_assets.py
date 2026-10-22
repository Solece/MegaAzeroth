"""Independently validate packaged runtime file closure and decoded buffer sizes."""
from pathlib import Path
import argparse,collections,json,math,struct,sys

def validate(root):
 checks=collections.Counter();rgba={};fail=[]
 def require(value,message):
  checks['checks']+=1
  if not value:fail.append(message)
 def existing(parent,name,expected=None):
  p=parent/name
  require(p.is_file(),'Missing referenced '+str(p.relative_to(root)))
  if p.is_file() and expected is not None:require(p.stat().st_size==expected,'Buffer size '+str(p.relative_to(root)))
  return p
 def texture(parent,name,w,h):
  existing(parent,name,w*h*4);rgba[(parent/name).resolve()]=(w,h)
 for p in root.rglob('*.json'):
  d=json.loads(p.read_text(encoding='utf-8-sig'));folder=p.parent
  if not isinstance(d,dict):continue
  if p.name=='model.json' and 'vertexCount' in d:
   n=d['vertexCount'];checks['modelCount']+=1;require(len(d['positions'])==3*n,str(p)+' positions');require(len(d['uv'])==2*n,str(p)+' UV');require(len(d.get('normals',[])) in (0,3*n),str(p)+' normals');require(len(d['triangles'])%3==0 and all(0<=i<n for i in d['triangles']),str(p)+' triangles');require(all(math.isfinite(v) for v in d['positions']),str(p)+' finite positions')
   # Runtime Model.Load uses raw texture.rgba + dimensions, not optional PNG.
   existing(folder,'texture.rgba');existing(folder,'texture.rgba.json')
   for clip in d.get('animations',[]):
    checks['animationCount']+=1;existing(folder,clip['file'],clip['frameCount']*n*12)
    for key,size in [('attachmentsFile',24),('portraitFile',16),('gripFile',12)]:
     if key in clip:existing(folder,clip[key],clip['frameCount']*size)
  if p.name=='death-profile.json':
   checks['deathProfileCount']+=1
   for clip in d['animations']:existing(folder,clip['file'],clip['frameCount']*clip['vertexCount']*12)
  if p.name.endswith('.rgba.json'):
   texture(folder,p.name[:-5],d['width'],d['height'])
  if d.get('format') in ['azeroth-enemy-material-profile-v1','azeroth-elwynn-biome-v2']:
   textures=d['textures'].values() if isinstance(d['textures'],dict) else d['textures']
   for t in textures:texture(folder,t['file'],t['width'],t['height'])
  if p.name=='manifest.json' and d.get('format')=='azeroth-spell-vfx-v1':
   checks['vfxManifestCount']+=1
   for t in d['textures']:texture(folder,t['key']+'.rgba',t['width'],t['height'])
   for spell in d['spells']:
    for effect in spell.get('effects',[]):existing(folder,effect['key']+'.json')
  if 'geometry' in d and isinstance(d['geometry'],dict):
   g=d['geometry'];checks['vfxGeometryCount']+=1
   if g.get('file'):existing(folder,g['file'],g['frameCount']*g['vertexCount']*12)
  if d.get('bonePoses'):
   b=d['bonePoses'];existing(folder,b['file'],b['frameCount']*len(b['bones'])*64)
  if p.name=='manifest.json' and d.get('format')=='azeroth-elwynn-biome-v2':
   for kind in ['props','buildings']:
    for entry in d[kind].values():existing(folder,entry['file'])
 for p in root.rglob('*.size'):
  w,h=map(int,p.read_text().split());texture(p.parent,p.with_suffix('.rgba').name,w,h);checks['uiTextureCount']+=1
 # Converted auxiliary atlases retain PNG IHDR dimensions even when they are
 # not referenced by the current renderer (source-textures audit extras).
 for p in root.rglob('*.rgba'):
  if p.resolve() not in rgba and p.with_suffix('.png').is_file():
   header=p.with_suffix('.png').read_bytes()[:24];require(header[:8]==b'\x89PNG\r\n\x1a\n','PNG signature '+str(p));w,h=struct.unpack('>II',header[16:24]);texture(p.parent,p.name,w,h)
 all_rgba={p.resolve() for p in root.rglob('*.rgba')};unknown=sorted(str(p.relative_to(root.resolve())) for p in all_rgba-set(rgba))
 # A source profile can use a different format revision; every unknown file is
 # surfaced explicitly, never counted as a validated texture by extension alone.
 require(not unknown,'RGBA dimensions missing from supported schema: '+repr(unknown))
 return {'Format':'megaazeroth-prepared-assets-closure-v1','Passed':not fail,'Counts':dict(checks),'CheckedRgbaFiles':len(rgba),'UnvalidatedRgba':unknown,'Failures':fail,'NativeGameplayVerified':False}
if __name__=='__main__':
 p=argparse.ArgumentParser(__doc__);p.add_argument('--runtime',type=Path,required=True);p.add_argument('--report',type=Path,required=True);a=p.parse_args()
 try:r=validate(a.runtime.resolve());a.report.write_text(json.dumps(r,indent=2)+'\n',encoding='utf8');print(json.dumps({k:v for k,v in r.items() if k!='Failures'},indent=2));print('\n'.join(r['Failures'][:10]));sys.exit(0 if r['Passed'] else 1)
 except Exception as e:print('Validation failed: '+str(e),file=sys.stderr);sys.exit(2)
