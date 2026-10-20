import sys,struct,json,re,hashlib,os
from pathlib import Path
sys.path.insert(0,r'D:/Games/Saitan/AzerothMegabonkLab/scripts/azeroth-assets')
from mpq_source import Archives
source=Archives()
def dbc(name):
 b,arc=source.read('DBFilesClient\\'+name+'.dbc');n,f,size,ss=struct.unpack_from('<4I',b,4);strings=b[20+n*size:]
 def text(off):return strings[off:strings.find(b'\0',off)].decode('utf8','replace') if 0<=off<len(strings) else ''
 return {r[0]:r for r in (struct.unpack_from('<'+'I'*f,b,20+i*size) for i in range(n))},text,dict(archive=arc,records=n,fields=f,size=size)
spells,_,spmeta=dbc('Spell');visuals,_,vmeta=dbc('SpellVisual');kits,_,kmeta=dbc('SpellVisualKit');effects,etext,emeta=dbc('SpellVisualEffectName')
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
work=Path(os.environ.get('AZEROTH_VFX_WORK',str(root/'private-assets/azeroth-vfx')));work.mkdir(parents=True,exist_ok=True)
raw=json.loads((root/'reports/megabonk-research/azeroth-ability-table-raw.json').read_text())
mage=['fireball','frostbolt','arcane_missiles','frost_nova','arcane_explosion','blizzard','flamestrike','fire_blast','cone_of_cold','blast_wave','pyroblast','ice_barrier','polymorph']
result=[]
for ability in raw['tableRows']:
 if ability['id'] not in mage:continue
 spellid=int(ability['words'][0],16);row=spells.get(spellid)
 if row is None:result.append(dict(id=ability['id'],error='Spell ID missing',spellId=spellid));continue
 vid=row[115];visual=visuals.get(vid);linked=[]
 if visual:
  if visual[6] and visual[7] in effects:linked.append(dict(stage='missile',effectId=visual[7],effect=effects[visual[7]],path=etext(effects[visual[7]][2])))
  if visual[11] and visual[12] in effects:linked.append(dict(stage='area',effectId=visual[12],effect=effects[visual[12]],path=etext(effects[visual[12]][2])))
  for stage,index in [('precast',1),('cast',2),('impact',3),('channel',5),('aura',4),('areaKit',13)]:
   kit=kits.get(visual[index])
   if not kit:continue
   for slot,eid in enumerate(kit[3:15],3):
    if eid in effects:linked.append(dict(stage=stage,slot=slot,kitId=kit[0],effectId=eid,effect=effects[eid],path=etext(effects[eid][2])))
 for l in linked:
  p=re.sub(r'\.(mdx|mdl)$','.m2',l['path'],flags=re.I)
  try:
   b,arc=source.read(p);l.update(available=True,source=p,archive=arc,bytes=len(b),version=struct.unpack_from('<I',b,4)[0],vertices=struct.unpack_from('<I',b,68)[0],textures=struct.unpack_from('<I',b,92)[0],header=struct.unpack_from('<84I',b,0),sha256=hashlib.sha256(b).hexdigest())
  except Exception as e:l.update(available=False,error=str(e))
 result.append(dict(id=ability['id'],spellId=spellid,visualId=vid,visual=visual,effects=linked))
out=dict(tables=dict(Spell=spmeta,SpellVisual=vmeta,SpellVisualKit=kmeta,SpellVisualEffectName=emeta),spells=result)
(work/'source-vfx.json').write_text(json.dumps(out,indent=2))
print(json.dumps(dict(tables=out['tables'],spells=[dict(id=r['id'],visualId=r.get('visualId'),effects=[dict(stage=l['stage'],path=l['path'],available=l['available'],vertices=l.get('vertices'),textures=l.get('textures')) for l in r.get('effects',[])]) for r in result]),indent=2))
