"""Validate local output binary lengths, geometry indices, textures and animations."""
import json,hashlib
import numpy as np
from PIL import Image
from mpq_source import ROOT,Archives
from convert_m2 import arr

def validate():
 results=[]
 for name in ['boar','skeleton','murloc','wolf','kobold','gnoll','tarantula']:
  folder=ROOT/'private-assets/azeroth'/name;d=json.loads((folder/'model.json').read_text());n=d['vertexCount'];p=np.asarray(d['positions']).reshape(-1,3);normal=np.asarray(d['normals']).reshape(-1,3);uv=np.asarray(d['uv']).reshape(-1,2);tri=np.asarray(d['triangles']).reshape(-1,3)
  assert p.shape==(n,3) and normal.shape==(n,3) and uv.shape==(n,2)
  assert tri.min()>=0 and tri.max()<n and np.isfinite(p).all() and np.isfinite(uv).all()
  tex=Image.open(folder/d['texture']);tex.load();assert tex.mode=='RGBA'
  checks=[]
  low=np.asarray(d['boundsMin']);high=np.asarray(d['boundsMax']);assert low.shape==(3,) and high.shape==(3,) and (low<high).all();assert (p>=low-1e-6).all() and (p<=high+1e-6).all()
  for a in d['animations']:
   f=folder/a['file'];assert f.stat().st_size==a['frameCount']*n*3*4
   frames=np.fromfile(f,dtype='<f4').reshape(-1,n,3);assert np.isfinite(frames).all()
   assert np.max(np.abs(frames))<1000
   assert (frames>=low-1e-6).all() and (frames<=high+1e-6).all()
   checks.append(dict(name=a['name'],bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest(),loopClosureMaxDistance=float(np.max(np.linalg.norm(frames[-1]-frames[0],axis=1)))))
  results.append(dict(name=name,vertexCount=n,triangleCount=len(tri),textureSize=tex.size,textureMode=tex.mode,boundsMin=low.tolist(),boundsMax=high.tolist(),allAnimationFramesInsideBounds=True,animations=checks,passAll=True))
 source=Archives();known=[]
 for name in ['Boar','Murloc','Skeleton','Wolf','Kobold','Gnoll','Tarantula']:
  known.append(f'Creature\\{name}\\{name}.m2')
 known += [r'DBFilesClient\CreatureModelData.dbc',r'DBFilesClient\CreatureDisplayInfo.dbc',r'Creature\Boar\BoarSkinBrown.blp',r'Creature\Skeleton\SkeletonSkin.blp',r'Creature\Murloc\Sahauginskin.blp']
 inventory=[]
 for path in known:
  occurrences=[]
  for arc,a in source.archives:
   h=a.get_hash_table_entry(path)
   if h is not None:
    block=a.block_table[h.block_table_index];occurrences.append(dict(archive=arc,bytes=block.size,flags=hex(block.flags)))
  b,chosen=source.read(path);inventory.append(dict(path=path,occurrencesLowToHigh=occurrences,selectedArchive=chosen,magic=b[:4].decode('ascii','replace'),sha256=hashlib.sha256(b).hexdigest()))
 report=dict(validation=results,knownPathInventory=inventory,archiveOrderAssumedLowToHigh=[a for a,_ in source.archives],note='Internal listfiles are empty. This is a verified inventory of named assets only; order assumption does not change selected files because all named assets occur only in patch.MPQ.')
 (ROOT/'reports/azeroth-assets/validated-pack.json').write_text(json.dumps(report,indent=2));print(json.dumps(results,indent=2))
if __name__=='__main__':validate()

