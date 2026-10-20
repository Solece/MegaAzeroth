from pathlib import Path
import mpyq,struct,json,collections
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
DATA=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/Data')
class Archives:
 def __init__(self): self.archives=[(name,mpyq.MPQArchive(str(DATA/name),listfile=False)) for name in ['patch.MPQ']+[f'patch-{i}.MPQ' for i in range(2,8)]]
 def read(self,path):
  for name,a in reversed(self.archives):
   h=a.get_hash_table_entry(path)
   if h is None: continue
   b=a.block_table[h.block_table_index]
   if not b.flags & 0x80000000: continue
   if not b.flags & (0x200|0x100|0x10000):
    a.file.seek(a.header['offset']+b.offset); data=a.file.read(b.size)
   else: data=a.read_file(path)
   return data,name
  raise FileNotFoundError(path)
if __name__=='__main__':
 a=Archives(); result=[]
 for model in ['Murloc','Wolf','Boar','Skeleton']:
  p=f'Creature\\{model}\\{model}.m2'
  try:
   b,arc=a.read(p); out=ROOT/'private-assets/azeroth/raw'/Path(p.replace('\\','/')); out.parent.mkdir(parents=True,exist_ok=True); out.write_bytes(b)
   result.append(dict(path=p,archive=arc,size=len(b),magic=b[:4].decode(),version=struct.unpack_from('<I',b,4)[0],u32Header=list(struct.unpack_from('<84I',b,0))))
  except Exception as e: result.append(dict(path=p,error=str(e)))
 (ROOT/'reports/azeroth-assets/model-headers.json').write_text(json.dumps(result,indent=2)); print(json.dumps(result,indent=2))
