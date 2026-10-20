"""Retain source textures and validate local companion binary packs."""
import hashlib,json,struct,sys
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
sys.path.insert(0,str(ROOT/'scripts/azeroth-assets'))
from mpq_source import Archives
from convert_m2 import arr
from blp_decode import decode_blp
source=Archives()
for key in ['sheep','waterelemental']:
 out=ROOT/'private-assets/azeroth-mage'/key;d=json.loads((out/'model.json').read_text());n=d['vertexCount']
 assert len(d['positions'])==n*3 and len(d['normals'])==n*3 and len(d['uv'])==n*2
 tri=np.asarray(d['triangles']);assert len(tri)%3==0 and tri.min()>=0 and tri.max()<n
 assert np.isfinite(d['positions']).all() and np.isfinite(d['uv']).all()
 checks=[]
 for c in d['animations']:
  raw=(out/c['file']).read_bytes();assert len(raw)==c['frameCount']*n*12 and np.isfinite(np.frombuffer(raw,dtype='<f4')).all();checks.append(dict(name=c['name'],animationId=c['animationId'],frames=c['frameCount'],loop=c['loop']))
 tm=json.loads((out/'texture.rgba.json').read_text());assert len((out/'texture.rgba').read_bytes())==tm['width']*tm['height']*4
 textures=[];b=(out/'original.m2').read_bytes();(out/'source-textures').mkdir(exist_ok=True)
 for i,(typ,flags,length,off) in enumerate(arr(b,92,'4I')):
  path=d['source']['texture'] if typ==11 else b[off:off+length].rstrip(b'\0').decode('ascii')
  raw,arc=source.read(path);im=decode_blp(raw);stem=f'source-textures/{i:02d}';im.save(out/(stem+'.png'));(out/(stem+'.rgba')).write_bytes(im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes());meta=dict(width=im.width,height=im.height,format='RGBA32',rowOrder='bottom-up');(out/(stem+'.rgba.json')).write_text(json.dumps(meta));textures.append(dict(index=i,replaceableType=typ,flags=flags,path=path,archive=arc,sha256=hashlib.sha256(raw).hexdigest(),png=stem+'.png',rgba=stem+'.rgba',alphaExtrema=im.getchannel('A').getextrema(),**meta))
 evidence=dict(status='PASS',sourceDisplayId=d['source']['displayId'],model=d['source']['model'],vertices=n,triangles=len(tri)//3,clips=checks,textures=textures,runtimeStatus='Not launched by asset worker',limitations='First diffuse pass in single atlas; original model/secondary texture inventory retained. Reflection/UV material animation/embedded particles require separate engine adaptation.' if key=='waterelemental' else '')
 (out/'validation.json').write_text(json.dumps(evidence,indent=2));print(key,'PASS',n,len(tri)//3,'clips',len(checks),'source textures',len(textures))
