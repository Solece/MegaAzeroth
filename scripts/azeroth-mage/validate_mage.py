"""Structural checks on the exported original Mage assets."""
from pathlib import Path
import hashlib,json,struct
import numpy as np
from PIL import Image
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
out=ROOT/'private-assets/azeroth-mage/human-male'
d=json.loads((out/'model.json').read_text());n=d['vertexCount']
assert n>500 and len(d['positions'])==n*3 and len(d['normals'])==n*3 and len(d['uv'])==n*2
tri=np.array(d['triangles']);assert len(tri)%3==0 and tri.min()>=0 and tri.max()<n
assert all(np.isfinite(d[k]).all() for k in ['positions','normals','uv'])
assert d['source']['raceId']==1 and d['source']['classId']==8 and d['source']['sexId']==0
assert any(e['itemId']==56 and e['displayId']==12647 for e in d['source']['equipment'])
assert Image.open(out/'texture.png').size==(256,256) and len((out/'texture.rgba').read_bytes())==256*256*4
assert Image.open(out/'texture.png').getchannel('A').getextrema()==(255,255)
results=[]
for c in d['animations']:
 raw=(out/c['file']).read_bytes();assert len(raw)==c['frameCount']*n*12;v=np.frombuffer(raw,dtype='<f4').reshape(-1,n,3);assert np.isfinite(v).all()
 a=(out/c['attachmentsFile']).read_bytes();assert len(a)==c['frameCount']*2*12;assert np.isfinite(np.frombuffer(a,dtype='<f4')).all();assert c['attachmentIds']==[21,22]
 assert c['maxVertexDisplacement']>0
 results.append(dict(name=c['name'],frames=c['frameCount'],animationId=c['animationId'],geometryBytes=len(raw),attachmentBytes=len(a)))
report=dict(status='PASS',vertexCount=n,triangleCount=len(tri)//3,sourceEquipment=d['source']['equipment'],clips=results,textureSha256=hashlib.sha256((out/'texture.rgba').read_bytes()).hexdigest(),runtimeStatus='Not executed by asset worker; integration test owned by parent.')
(out/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
