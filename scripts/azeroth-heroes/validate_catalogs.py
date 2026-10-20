"""Independent bounded byte-for-byte validation of the all-class source export."""
from pathlib import Path
import argparse,hashlib,json,math,struct
import pefile

ROOT=Path(__file__).resolve().parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--input',type=Path,required=True,help='Private source export directory')
parser.add_argument('--report',type=Path,required=True,help='Validation report output path')
args=parser.parse_args()
OUT=args.input
EXE=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/AzerothSurvivors.exe')
b=EXE.read_bytes();pe=pefile.PE(data=b,fast_load=True);base=pe.OPTIONAL_HEADER.ImageBase
checks=0
def check(condition,message):
 global checks
 assert condition,message
 checks+=1
def load(path):return json.loads(path.read_text(encoding='utf8'))
def raw(pointer):
 check(base<=pointer<base+pe.OPTIONAL_HEADER.SizeOfImage,'pointer bounds')
 return pe.get_offset_from_rva(pointer-base)
def txt(offset):
 p,n=struct.unpack_from('<QQ',b,offset);check(n<10000,'bounded string')
 return b[raw(p):raw(p)+n].decode('utf8') if n else ''
def bits(f):return struct.pack('<f',f)
def provenance(item):
 off=int(item['rawOffset'],16);n=item['byteLength']
 check(0<=n<=100000 and 0<=off<=len(b)-n,'bounded provenance')
 segment=b[off:off+n]
 check(segment.hex()==item['bytesHex'],'record bytes')
 check(hashlib.sha256(segment).hexdigest()==item['sha256'],'record sha')

summary=load(OUT/'classes.json')
check(hashlib.sha256(b).hexdigest()==summary['sourceSha256'],'version sha')
check(summary['classCount']==9 and summary['treeCount']==27 and summary['talentCount']==354,'registry counts')
check(summary['classSpellCount']==102 and summary['abilityTableCount']==112,'spell count')
all_spells={};all_talents=[];effects=[]
numeric={'baseDamage':1,'baseCooldown':2,'range':3,'radius':4,'duration':6,'projectileSpeed':8,'statusValue':11,'statusDuration':12}
integer={'sourceSpellId':0,'targets':5,'ticks':7,'statusKind':10}
for index,c in enumerate(summary['classes']):
 cid=c['id'];off=int(c['sourceOffset'],16)
 check(off==0x6b867c0+index*144 and txt(off)==cid,'class row')
 abilities=load(OUT/'abilities'/f'{cid}.json');talents=load(OUT/'talents'/f'{cid}.json')
 check(abilities['sourceSha256']==summary['sourceSha256'],'catalog sha')
 check(abilities['startingSpell']==txt(off+48) and abilities['startingSpell'] in c['spellIds'],'starter')
 check([s['id'] for s in abilities['spells']]==c['spellIds'],'class membership')
 check(len(talents['talents']) in [39,40] and len(talents['trees'])==3,'talent trees')
 check(len({t['id'] for t in talents['talents']})==len(talents['talents']),'unique talent ids')
 weapon=c['sourceAutoWeapon'];flag,damage,cd,reach=struct.unpack_from('<I3f',b,off-16)
 check(weapon['enabled']==bool(flag) and flag in [0,1],'auto weapon optional flag')
 if flag:
  check(all(bits(weapon[k])==bits(v) for k,v in [('damage',damage),('cooldown',cd),('range',reach)]),'auto weapon values')
 check(weapon['enabled']==(cid in ['warrior','paladin','rogue']),'auto weapon classes')
 for s in abilities['spells']:
  check(s['id'] not in all_spells,'unique spell membership');all_spells[s['id']]=s
  row=int(s['sourceOffset'],16);check(txt(row)==s['id'] and txt(row+16)==s['name'] and txt(row+32)==s['icon'],'spell strings')
  u=struct.unpack_from('<14I',b,row+64);f=struct.unpack_from('<14f',b,row+64)
  for key,i in numeric.items():check(bits(s[key])==bits(f[i]),'source spell f32 '+key)
  for key,i in integer.items():check(s[key]==u[i],'source spell int '+key)
  check(s['castKindId']==(u[13]>>8&255),'cast kind')
  p,n=struct.unpack_from('<QQ',b,row+48);allowed=list(b[raw(p):raw(p)+n]) if n else []
  check(s['allowedUpgrades']==allowed,'allowed upgrades')
  check([int(q['id']) for q in s['upgrades']]==allowed,'normalized upgrade membership')
  for q in s['upgrades']:check(q['sourceEvidence'] and len(q['rarityValues'])==5 and q['value']==q['rarityValues'][0],'upgrade evidence tiers')
 for t in talents['talents']:
  all_talents.append(t);row=int(t['sourceOffset'],16)
  check(txt(row)==t['id'] and txt(row+64)==t['description'],'talent strings')
  check(list(b[row+96:row+99])==[t['tier'],t['column'],t['maxRank']],'talent placement/ranks')
  for e in t['effects']:
   effects.append(e);row=int(e['sourceOffset'],16);u=struct.unpack_from('<16I',b,row)
   check(e['tag']==u[0]&255 and e['rawPackedTag']==u[0],'effect tag full payload')
   check([hex(v) for v in u]==e['rawWords'],'effect payload exact')
   check(not e['kind'].startswith('sourceEffect'),'decoded effect family')
   if e['tag']==1:check(e['count']==u[1] and e['maxPerSecond']==u[2] and e['triggerEvent']==u[8],'counter event offsets')
   if e['tag']==0:check(e['triggerEvent']==u[8] and bits(e['internalCooldown'])==b[row+8:row+12],'proc event offsets')
   if e['tag']==8:
    check(e['status']==struct.unpack_from('<i',b,row+8)[0],'signed hit status including sentinel minus one')
    check(bits(e['statusValue'])==b[row+12:row+16] and bits(e['statusDuration'])==b[row+16:row+20],'hit status amount and duration are separate')
   if e['tag']==9:
    check(e['triggerEvent']==u[4] and u[4] in [0,2],'talent DoT trigger event is not tick count')
    check(e['ticks']==max(1,math.floor(e['duration']+.5)) and e['interval']==1,'source talent DoT one-second schedule')
   if e['tag']==6:check(e['radius']==10 and e['sourceRadiusEvidence'],'source cleave radius')
   if e.get('petSourceOffset'):provenance(e['petProvenance'])
check(len(all_spells)==102 and len(all_talents)==354,'final totals')
check(len({e['tag'] for e in effects})==28,'effect family count')
for name in ['classes','spells','talents','effects']:
 evidence=load(OUT/'evidence'/f'{name}.json')
 for item in evidence.values() if isinstance(evidence,dict) else evidence:provenance(item)
enums=load(OUT/'source-semantic-enums.json')
check(len(enums['castKinds'])==12 and len(enums['statusKinds'])==13,'semantic enums')
old=load(Path(r'D:/Games/Saitan/AzerothMegabonkLab/src/AzerothAbilities/mage.json'))
new=load(OUT/'abilities/mage.json')
check(old['upgradeRules']==new['upgradeRules'],'existing verified upgrade rules preserved')
for old_spell,new_spell in zip(old['spells'],new['spells']):
 for key,value in old_spell.items():
  if key not in ['upgrades']:check(new_spell[key]==value,'Mage field preserved '+old_spell['id']+'/'+key)
def weights(luck,level=1,extra=0):
 u=1+luck+min(level*.01,.30)+extra
 return [60,25*u,10*u*u,4*u*u*u,u*u*u*u]
before=weights(0);after=weights(1)
check(after[-1]/sum(after)>before[-1]/sum(before),'luck raises source legendary probability')
report=dict(passed=True,checks=checks,classCount=9,classSpells=102,totalSourceSpells=112,talents=354,effectFamilies=28,
 sourceSha256=summary['sourceSha256'],nativeGameplayVerification=False,sourceRaritySamples=dict(level=1,luck0Weights=before,luck1Weights=after))
args.report.write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(report,indent=2))
