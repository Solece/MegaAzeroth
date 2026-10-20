from pathlib import Path
import json,re,sys,hashlib,struct
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab'); exe=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/AzerothSurvivors.exe'); b=exe.read_bytes(); out=root/'reports/azeroth-ui'
pos=b.index(b'{\n  "game.title": "Azeroth Survivors"')
s=b[pos:pos+400000].decode('utf8',errors='replace'); d,n=json.JSONDecoder().raw_decode(s)
(out/'original-en.json').write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf8')
abilities={k:v for k,v in d.items() if k.startswith(('ability.','upgrade.','talent.','class.','hud.','levelup.','spell.'))}
(out/'original-abilities-ui-en.json').write_text(json.dumps(abilities,ensure_ascii=False,indent=2),encoding='utf8')
# Preserve UTF8 text around native spell and talent data, never interpret binary bytes as trustworthy numbers.
for name,start,end in [('class-spell-constants',0x6b85900,0x6b88000),('talent-constants',0x7166000,0x717c000),('embedded-ui-overrides',0x7164c00,0x7166100)]:
 chunk=b[start:end]; lines=[]
 for m in re.finditer(rb'[\x09\x0A\x0D\x20-\x7E]{4,}',chunk): lines.append(hex(start+m.start())+' '+m.group().decode())
 (out/(name+'.txt')).write_text('\n'.join(lines),encoding='utf8')
info={'source':str(exe),'sha256':hashlib.sha256(b).hexdigest(),'englishJsonOffset':hex(pos),'englishJsonByteLength':len(s[:n].encode()),'keys':len(d),'selectedKeys':len(abilities),'gameplayVersion':'not established; updater user-agent 0.2.0 is not proof'}
(out/'original-source-manifest.json').write_text(json.dumps(info,indent=2))
print(info)
print(json.dumps({k:v for k,v in d.items() if k.startswith(('ability.fireball','upgrade.','hud.','levelup.'))},indent=2))
