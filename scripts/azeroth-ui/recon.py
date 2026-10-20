from pathlib import Path
import re,json,mpyq
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
install=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors')
b=(install/'AzerothSurvivors.exe').read_bytes()
strings=[(m.start(),m.group().decode('ascii')) for m in re.finditer(rb'[\x20-\x7E]{6,}',b)]
needles=['survivors','Fireball','Frostbolt','Whirlwind','Shadow Bolt','Lightning Bolt','spell','survivor','levelup','level_up','tooltip','bevy','version','talent']
found=[{'offset':hex(o),'text':s[:60000]} for o,s in strings if any(t.lower() in s.lower() for t in needles)]
(root/'reports/azeroth-ui/exe-strings-selected.json').write_text(json.dumps(found,indent=2),encoding='utf8')
(root/'reports/azeroth-ui/exe-strings-all.txt').write_text('\n'.join(hex(o)+' '+s for o,s in strings),encoding='utf8')
print('strings',len(strings),'selected',len(found))
for x in found:
 if any(t.lower() in x['text'].lower() for t in ['Fireball','Frostbolt','Whirlwind','Shadow Bolt','Lightning Bolt','survivors']): print(x['offset'], x['text'][:3500])
names=[r'Interface\Tooltips\UI-Tooltip-Border.blp',r'Interface\Tooltips\UI-Tooltip-Background.blp',r'Interface\Buttons\UI-EmptySlot.blp',r'Interface\Buttons\UI-Quickslot2.blp',r'Interface\Buttons\UI-Quickslot.blp',r'Interface\TargetingFrame\UI-StatusBar.blp',r'Interface\TargetingFrame\UI-TargetingFrame.blp',r'Interface\PlayerFrame\UI-PlayerFrame.blp',r'Fonts\FRIZQT__.TTF',r'DBFilesClient\Spell.dbc',r'DBFilesClient\SpellIcon.dbc']
a={f.name:mpyq.MPQArchive(str(f)) for f in (install/'Data').glob('*.MPQ')}
results=[]
for name in names:
 for file,arc in a.items():
  try:
   d=arc.read_file(name)
   if d:
    target=root/'private-assets/azeroth-ui'/file/name.replace('\\','/')
    target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(d)
    results.append({'name':name,'archive':file,'bytes':len(d),'magic':d[:8].hex(),'output':str(target)})
  except Exception as e: results.append({'name':name,'archive':file,'error':str(e)})
(root/'reports/azeroth-ui/mpq-known-ui.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results,indent=2))
