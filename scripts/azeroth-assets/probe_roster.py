import json,struct
from mpq_source import Archives,ROOT
from convert_m2 import arr
a=Archives()
def dbc(name):
 b,arc=a.read(f'DBFilesClient\\{name}.dbc');n,nf,stride,ss=struct.unpack_from('<4I',b,4);strings=b[20+n*stride:]
 def st(off):return strings[off:strings.find(b'\0',off)].decode('utf8','replace')
 return [struct.unpack_from('<'+'I'*nf,b,20+i*stride) for i in range(n)],st
models,mstr=dbc('CreatureModelData');displays,dstr=dbc('CreatureDisplayInfo');out=[]
for m in models:
 path=mstr(m[2])
 if any(t in path.lower() for t in ['creature\\wolf\\','creature\\kobold\\','creature\\gnoll\\','creature\\spider\\','creature\\tarantula\\','creature\\giantspider\\']):
  p=path[:-4]+'.m2'
  try:
   b,archive=a.read(p);textures=[]
   for typ,flags,length,off in arr(b,92,'4I'):textures.append(dict(type=typ,flags=flags,filename=b[off:off+length].rstrip(b'\0').decode('utf8','replace')))
   viewoff=struct.unpack_from('<I',b,80)[0]
   out.append(dict(modelId=m[0],path=p,archive=archive,textures=textures,submeshes=arr(b,viewoff+24,'10H3f'),textureUnits=arr(b,viewoff+32,'12H'),textureLookup=arr(b,148,'H'),displays=[dict(displayId=r[0],skins=[dstr(o) for o in r[6:9]]) for r in displays if r[1]==m[0]]))
  except Exception as e:out.append(dict(modelId=m[0],path=p,error=repr(e)))
(ROOT/'reports/azeroth-assets/roster-candidates.json').write_text(json.dumps(out,indent=2))
for r in out:
 print(r['path'], 'error='+r['error'] if 'error'in r else dict(textures=r['textures'],submeshCount=len(r['submeshes']),displays=r['displays'][:6],lookup=r['textureLookup'],units=r['textureUnits']))

