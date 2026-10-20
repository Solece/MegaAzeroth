from pathlib import Path
import mpyq,json,struct,collections
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
data=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/Data')
overlay={}; archive_info=[]; ar={}
for name in ['patch.MPQ']+[f'patch-{i}.MPQ' for i in range(2,8)]:
 a=mpyq.MPQArchive(str(data/name)); ar[name]=a
 files=[f.decode('utf-8','replace') if isinstance(f,bytes) else f for f in a.files]
 archive_info.append({'archive':name,'listedFiles':len(files),'extensions':dict(collections.Counter(Path(f).suffix.lower() for f in files))})
 for f in files: overlay[f.lower()]={'path':f,'archive':name}
(root/'reports/azeroth-assets/mpq-inventory.json').write_text(json.dumps({'assumedPriorityLowToHigh':list(ar),'archives':archive_info,'overlay':overlay},indent=2))
candidates=[]
for key,x in overlay.items():
 if key.endswith(('.m2','.mdx')) and any(t in key for t in ['murloc','boar','wolf','skeleton']):
  try:
   b=ar[x['archive']].read_file(x['path']); x=dict(x,bytes=len(b),magic=b[:4].decode('ascii','replace'),version=struct.unpack_from('<I',b,4)[0]); candidates.append(x)
  except Exception as e: candidates.append(dict(x,error=str(e)))
(root/'reports/azeroth-assets/candidates.json').write_text(json.dumps(candidates,indent=2))
print(json.dumps(archive_info,indent=2)); print(json.dumps(candidates[:70],indent=2))
