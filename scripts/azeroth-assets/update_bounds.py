"""Backfill animation union bounds without rewriting meshes/animation files."""
import json,os
import numpy as np
from mpq_source import ROOT
for folder in sorted((ROOT/'private-assets/azeroth').iterdir()):
 target=folder/'model.json'
 if not target.exists():continue
 d=json.loads(target.read_text());n=d['vertexCount'];p=np.asarray(d['positions']).reshape(-1,3);low=p.min(0);high=p.max(0)
 for a in d['animations']:
  f=np.fromfile(folder/a['file'],dtype='<f4').reshape(-1,n,3);assert np.isfinite(f).all();low=np.minimum(low,f.min((0,1)));high=np.maximum(high,f.max((0,1)))
 d['boundsMin']=low.tolist();d['boundsMax']=high.tolist();tmp=folder/'model.json.bounds-tmp';tmp.write_text(json.dumps(d,separators=(',',':')));os.replace(tmp,target);print(folder.name,d['boundsMin'],d['boundsMax'])
