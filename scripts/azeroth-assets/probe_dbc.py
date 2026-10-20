from mpq_source import *
a=Archives(); out=[]
for name in ['CreatureModelData','CreatureDisplayInfo']:
 b,arc=a.read(f'DBFilesClient\\{name}.dbc'); n,fields,size,ss=struct.unpack_from('<4I',b,4); strings=b[20+n*size:]; rows=[struct.unpack_from('<'+'I'*fields,b,20+i*size) for i in range(n)]
 def st(off):
  if off>=len(strings): return ''
  return strings[off:strings.find(b'\0',off)].decode('utf8','replace')
 if name=='CreatureModelData':
  matches=[dict(fields=r,strings=[st(x) for x in r if 0<x<len(strings) and any(v in st(x).lower() for v in ['creature\\boar\\boar','creature\\murloc\\murloc','creature\\skeleton\\skeleton','creature\\wolf\\wolf'])]) for r in rows if any(v in st(r[2]).lower() for v in ['creature\\boar\\boar','creature\\murloc\\murloc','creature\\skeleton\\skeleton','creature\\wolf\\wolf'])]; ids={m['fields'][0] for m in matches}
 else:
  matches=[dict(fields=r,skins=[st(x) for x in r[6:9]]) for r in rows if r[1] in ids]
 out.append(dict(name=name,archive=arc,magic=b[:4].decode(),records=n,fieldCount=fields,recordSize=size,stringSize=ss,matches=matches))
print(json.dumps(out,indent=2)); (ROOT/'reports/azeroth-assets/creature-dbc.json').write_text(json.dumps(out,indent=2))
