from pathlib import Path
import sys,json,io
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'azeroth-assets'))
from mpq_source import Archives
from PIL import Image
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab'); out=root/'private-assets/azeroth-ui/verified'; out.mkdir(parents=True,exist_ok=True)
a=Archives(); rows=[]
names=[r'Interface\Tooltips\UI-Tooltip-Border.blp',r'Interface\Tooltips\UI-Tooltip-Background.blp',r'Interface\Buttons\UI-EmptySlot.blp',r'Interface\Buttons\UI-Quickslot2.blp',r'Interface\Buttons\UI-Quickslot.blp',r'Interface\TargetingFrame\UI-StatusBar.blp',r'Interface\TargetingFrame\UI-TargetingFrame.blp',r'Interface\TargetingFrame\UI-TargetingFrame-BarFill.blp',r'Interface\TargetingFrame\UI-TargetingFrame-Dead.blp',r'Interface\PlayerFrame\UI-PlayerFrame.blp',r'Interface\CharacterFrame\UI-Character-Tabard-Human.blp',r'Interface\MainMenuBar\UI-MainMenuBar-EndCap-Human.blp',r'Fonts\FRIZQT__.TTF']
for icon in ['Spell_Fire_FlameBolt','Spell_Frost_FrostBolt02','Spell_Nature_StarFall','Spell_Frost_FrostNova','Spell_Nature_WispSplode','Spell_Frost_IceStorm','Spell_Fire_SelfDestruct','Spell_Fire_Fireball','Spell_Frost_Glacier','Spell_Fire_Fireball02','Spell_Ice_Lament','Spell_Nature_Polymorph','INV_Misc_Book_09']:
 names.append('Interface\\Icons\\'+icon+'.blp')
for p in names:
 try:
  b,arc=a.read(p); t=out/Path(p.replace('\\','/')); t.parent.mkdir(parents=True,exist_ok=True);t.write_bytes(b)
  row={'source':p,'archive':arc,'bytes':len(b),'magic':b[:4].hex(),'target':str(t)}
  if b.startswith((b'BLP1',b'BLP2')):
   im=Image.open(io.BytesIO(b)); im.save(t.with_suffix('.png'));row['imageSize']=im.size
  rows.append(row)
 except Exception as e: rows.append({'source':p,'error':str(e)})
(root/'reports/azeroth-ui/verified-assets.json').write_text(json.dumps(rows,indent=2)); print(json.dumps(rows,indent=2))
