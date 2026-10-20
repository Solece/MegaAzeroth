import sys,io,json
from pathlib import Path
from PIL import Image
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab'); sys.path.insert(0,str(root/'scripts/azeroth-assets'));from mpq_source import Archives
a=Archives();out=root/'private-assets/azeroth-ui/verified';rows=[]
for p in ['Interface/TargetingFrame/UI-PlayerFrame.blp','Interface/MainMenuBar/UI-MainMenuBar-Human.blp','Interface/MainMenuBar/UI-MainMenuBar-Dwarf.blp','Interface/FrameXML/PlayerFrame.xml','Interface/FrameXML/MainMenuBar.xml','Interface/FrameXML/ActionButtonTemplate.xml','Interface/FrameXML/PlayerFrame.lua']:
 try:
  b,arc=a.read(p.replace('/','\\')); t=out/p;t.parent.mkdir(parents=True,exist_ok=True);t.write_bytes(b)
  if p.endswith('.blp'): Image.open(io.BytesIO(b)).save(t.with_suffix('.png'))
  rows.append({'source':p,'bytes':len(b),'archive':arc})
 except Exception as e: rows.append({'source':p,'error':str(e)})
(root/'reports/azeroth-ui/framexml-manifest.json').write_text(json.dumps(rows,indent=2)); print(json.dumps(rows,indent=2))
