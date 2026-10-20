"""Read-only extraction of Mage talents from the installed, hashed Azeroth binary."""
from pathlib import Path
import argparse,hashlib,json,struct
import pefile

p=argparse.ArgumentParser();p.add_argument('--exe',default=r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/AzerothSurvivors.exe');p.add_argument('--out',required=True);a=p.parse_args()
b=Path(a.exe).read_bytes();sha=hashlib.sha256(b).hexdigest()
assert sha=='2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a',sha
pe=pefile.PE(data=b,fast_load=True);base=pe.OPTIONAL_HEADER.ImageBase
def raw(ptr):return pe.get_offset_from_rva(ptr-base)
def text(off):
    ptr,n=struct.unpack_from('<QQ',b,off)
    return '' if ptr==0 or n==0 else b[raw(ptr):raw(ptr)+n].decode('utf8')
def filt(o):
    kind=struct.unpack_from('<I',b,o)[0]
    if kind==1:return {'spells':[text(o+8)]}
    if kind==2:
        ptr,n=struct.unpack_from('<QQ',b,o+8)
        return {'spells':[text(raw(ptr)+16*i) for i in range(n)]}
    return {'school':{0x203:'Fire',0x403:'Frost',0x603:'Arcane'}.get(kind,'all'),'rawFilter':kind}
rows=[]
for tree,start in [('Arcane',0x7175f50),('Fire',0x7176c68),('Frost',0x7177940)]:
    for n in range(13):
        r=start+n*104;ep,ec=struct.unpack_from('<QQ',b,r+48);effects=[]
        for i in range(ec):
            o=raw(ep)+64*i;u=struct.unpack_from('<16I',b,o);f=struct.unpack_from('<16f',b,o)
            e={'sourceOffset':hex(o),'tag':u[0],'rawWords':[hex(v) for v in u]}
            if u[0]==30:e.update(kind='stat',stat=u[2],value=f[1])
            elif u[0]==31:e.update(kind='spellModifier',value=f[8],modifier=u[9],**filt(o+8))
            elif u[0]==0:e.update(kind='castProc',chance=f[1],spell=text(o+16))
            elif u[0]==1:e.update(kind='criticalCounterCast',count=u[1],maxPerSecond=u[8],spell=text(o+16),**filt(o+40))
            elif u[0]==4:e.update(kind='impactSplash',value=f[1],radius=f[2],**filt(o+32))
            elif u[0]==6:e.update(kind='chain',targets=u[1],damageFactor=f[2],**filt(o+24))
            elif u[0]==8:e.update(kind='hitStatus',chance=f[1],status=u[2],duration=f[3],**filt(o+32))
            elif u[0]==9:e.update(kind='critDot',spellId=u[1],value=f[2],duration=f[3],ticks=u[4],**filt(o+24))
            elif u[0]==11:e.update(kind='killExplosion',value=f[1],radius=f[2],spellId=u[3],maxPerSecond=u[4],**filt(o+24))
            elif u[0]==24:e.update(kind='cascade',maxAdditional=u[1])
            elif u[0]==26:e.update(kind='periodicCast',interval=f[1],spell=text(o+8))
            elif u[0] in (27,28):e.update(kind='startingSpell',spell=text(o+8))
            elif u[0]==29:
                pet=raw(struct.unpack_from('<Q',b,o+8)[0]);entry,display=struct.unpack_from('<II',b,pet+16);scale,damage,cooldown=struct.unpack_from('<fff',b,pet+24);spell,school=struct.unpack_from('<II',b,pet+36)
                e.update(kind='summon',petSourceOffset=hex(pet),petName=text(pet),petCreatureEntry=entry,petDisplayId=display,petFloat24=scale,petFloat28=damage,petFloat32=cooldown,petSpellId=spell,petSchool=school)
            else:raise ValueError((hex(o),u))
            effects.append(e)
        tier,column,maxrank=b[r+96:r+99]
        rows.append({'id':text(r),'name':text(r+16),'icon':text(r+32),'description':text(r+64),'prerequisite':text(r+80),'tree':tree,'tier':tier,'column':column,'maxRank':maxrank,'requiredTreePoints':tier*5,'sourceOffset':hex(r),'effects':effects})
assert len(rows)==39 and len({r['id'] for r in rows})==39
out={'sourceSha256':sha,'sourceVersion':'v0.102.3 (f78e6fab)','source':a.exe,'nodeStride':104,'effectStride':64,'maxPoints':51,'pointXpFirst':80,'pointXpStep':15,'progressEvidence':['native145887D50..145887EE1','native145887F80..145888000','native14587D0F0..14587D482','native1402A4EB0..1402A4F3F'],'baselineReward':{'secondsCoefficient':.2,'normalizedKillsCoefficient':.1,'bossesCoefficient':40,'winBonus':200,'zoneIndex':0,'runOptionsMultiplier':1,'accountPerkMultiplier':1},'talents':rows}
Path(a.out).parent.mkdir(parents=True,exist_ok=True);Path(a.out).write_text(json.dumps(out,indent=2),encoding='utf8')
print(f'Extracted {len(rows)} source Mage talents; max51 points, threshold23205XP; SHA256 {sha}')
