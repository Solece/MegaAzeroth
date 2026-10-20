"""Read-only export of all verified playable class/spell/talent tables.

This converter pins the installed source version by SHA256. Runtime-ready fields
and raw byte provenance are separate; unresolved behavior remains explicit.
"""
from pathlib import Path
import argparse, collections, copy, hashlib, json, math, re, struct
import pefile

SOURCE_SHA = '2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a'
CLASS_IDS = ['warrior', 'paladin', 'hunter', 'rogue', 'priest', 'shaman', 'mage', 'warlock', 'druid']
SCHOOLS = ['Physical', 'Holy', 'Fire', 'Nature', 'Frost', 'Shadow', 'Arcane']
CAST_HINTS = {0:'projectile',1:'instant',2:'selfAoe',3:'groundAoe',4:'chain',5:'melee',6:'damageOverTime',7:'finisher',8:'cone',9:'selfBuff',10:'totem',11:'passive'}
MODIFIERS = ['damage','cooldown','targets','radius','duration']
STATUS_HINTS = ['none','slow','root','stun','fear','lifesteal','heal','shield','damageBuff','hasteBuff','damageDebuff','invulnerability','thorns']
EVENT_HINTS = {0:'hit',1:'hit',2:'criticalHit',3:'castWithCriticalHit',4:'damageOverTimeTick',6:'kill',7:'cast',8:'meleeDamageTaken',9:'healthBelow',10:'shieldBreak',11:'preventedDeath'}
STAT_HINTS = {0:'allDamage',1:'haste',2:'maximumHealth',3:'criticalChance',4:'criticalDamage',5:'damageReduction',6:'healthPerSecond',7:'movementSpeed',12:'luck',13:'echoChance',14:'lifesteal',15:'healthPerKill',16:'originalAutoWeaponDamage',17:'petDamage',18:'damageVsControlled',19:'damageVsElitesAndBosses',20:'damageVsBelowThirtyPercentHealth',22:'reflectMeleeDamage',24:'maximumHealthRegenPerSecond',25:'damagePerTenPercentReduction',26:'damageFromReductionOverCap',27:'additionalReductionCap',28:'damageWhileShielded'}
RACE_BONUS_HINTS = {0:'maximumHealth',1:'allDamage',2:'cooldownReduction',3:'movementSpeed',4:'criticalChance',5:'damageReduction',6:'healthPerSecond',7:'pickupRange',8:'experience',9:'gold',10:'luck',11:'healthPerKill'}
PASSIVE_TEXTS = {
 'polymorph':('Your spells have a {p}% chance to turn the enemy they hit into a sheep,','on every enemy within {r} yd.'),
 'faerie_fire':('Marks {whom} within {range} yd,','When a marked enemy dies, its mark moves to one nearby.'),
 'soul_fire':('Your kills gather soul shards,','for {dmg} Fire damage.'),
 'call_of_flame':('Your totems explode in flame when they expire,','at most once every {iv} sec.'),
 'lightwell':('Every {iv} sec, once enemies are within {near} yd,','Holy damage every second.'),
 'seal_fate':('Your strikes build combo points,','then the most wounded below 30% health.'),
 'hunters_mark':('Marks the toughest enemy within {range} yd:','and a second later marks the next.'),
 'redemption':('Enemies you slay have a {p}% chance to burst in holy light,','At most two bursts a second.'),
 'bloodrage':('Each of your hits fills {rage} rage,','Rage drains when you stop fighting.')}
PASSIVE_FLOORS={'polymorph':3,'faerie_fire':0,'soul_fire':3,'call_of_flame':1.5,'lightwell':6,'seal_fate':2,'hunters_mark':2,'redemption':0,'bloodrage':8}

def export(exe, repo, output):
 data = exe.read_bytes(); sha = hashlib.sha256(data).hexdigest(); assert sha == SOURCE_SHA, sha
 pe = pefile.PE(data=data, fast_load=True); image = pe.OPTIONAL_HEADER.ImageBase
 def raw(pointer):
  assert image <= pointer < image + pe.OPTIONAL_HEADER.SizeOfImage
  return pe.get_offset_from_rva(pointer - image)
 def ref(offset, width):
  assert 0 <= offset <= len(data) and 0 <= width <= 100000 and offset + width <= len(data)
  return dict(rawOffset=hex(offset), rva=hex(pe.get_rva_from_offset(offset)),
      va=hex(image + pe.get_rva_from_offset(offset)), byteLength=width,
      sha256=hashlib.sha256(data[offset:offset + width]).hexdigest(), bytesHex=data[offset:offset + width].hex())
 def text(offset):
  pointer, length = struct.unpack_from('<QQ', data, offset)
  if not length: return ''
  assert length < 10000
  start = raw(pointer); return data[start:start + length].decode('utf8')
 def string_slice(offset):
  pointer, count = struct.unpack_from('<QQ', data, offset)
  assert count < 10000
  return [text(raw(pointer) + i * 16) for i in range(count)]
 def filt(offset):
  packed = struct.unpack_from('<I', data, offset)[0]; kind = packed & 255
  if kind == 1: return dict(spells=[text(offset + 8)], filterDiscriminant=kind, filterPackedFlags=packed >> 8)
  if kind == 2: return dict(spells=string_slice(offset + 8), filterDiscriminant=kind, filterPackedFlags=packed >> 8)
  if kind == 3:
   school = (packed >> 8) & 255
   return dict(school=SCHOOLS[school], filterDiscriminant=kind, filterPackedFlags=packed >> 8,excludePets=True,
       filterSemanticEvidence='Native filter predicate VA145934311..14593431F compares school byte1 and rejects sourceKind2(pet)')
  if kind == 0:return dict(school='all', filterDiscriminant=kind, filterPackedFlags=packed >> 8)
  if kind == 4:
   cast_kind = packed >> 8 & 255
   assert cast_kind in CAST_HINTS
   return dict(school='sourceCastKind',filterDiscriminant=kind,filterPackedFlags=packed>>8,filterCastKind=cast_kind,
       excludePets=True,filterSemanticEvidence='Native filter4 VA14593445D compares spell+0x75 against filter+1, lookup excludes sourceKind2(pet) at14593432E..338. Direct match also1458ECC19..29')
  if kind == 6:
   return dict(school='sourcePetOnly',filterDiscriminant=kind,filterPackedFlags=packed>>8,petOnly=True,
       filterSemanticEvidence='Native filter6 jump table raw71866d8 -> VA145934324 returns sourceKind==2; all catalog rows explicitly describe pets/demons/bites')
  return dict(school=f'sourceFilter{kind}', filterDiscriminant=kind, filterPackedFlags=packed >> 8,
      filterSemanticStatus='pendingExactNativeFilterEvidence',filterRawWord=hex(packed))

 old = json.loads((repo / 'src/AzerothAbilities/mage.json').read_text(encoding='utf8'))
 old_spells = {s['id']:s for s in old['spells']}
 old_talents = json.loads((repo / 'src/MageTalents/mage-talents.json').read_text(encoding='utf8'))
 proven_talent_effects = {e['sourceOffset']:e for t in old_talents['talents'] for e in t['effects']}
 def pet_fields(effect_offset, pointer_offset):
  pet=raw(struct.unpack_from('<Q',data,effect_offset+pointer_offset)[0]);entry,display=struct.unpack_from('<II',data,pet+16)
  return dict(petSourceOffset=hex(pet),petName=text(pet),petCreatureEntry=entry,petDisplayId=display,
      petScale=struct.unpack_from('<f',data,pet+24)[0],petBaseDamage=struct.unpack_from('<f',data,pet+28)[0],petCooldown=struct.unpack_from('<f',data,pet+32)[0],
      petFloat24=struct.unpack_from('<f',data,pet+24)[0],petFloat28=struct.unpack_from('<f',data,pet+28)[0],petFloat32=struct.unpack_from('<f',data,pet+32)[0],
      petSpellId=struct.unpack_from('<I',data,pet+36)[0],petSchool=struct.unpack_from('<I',data,pet+40)[0],petProvenance=ref(pet,48))
 output.mkdir(parents=True, exist_ok=True)
 for folder in ['abilities', 'talents', 'evidence']: (output / folder).mkdir(exist_ok=True)
 classes = []; raw_classes = []; raw_spells = {}; raw_talents = {}; all_effects = []
 spell_rows = {}
 # Exact contiguous112-row table:102 class abilities,8 racials,2 talent-only spells.
 for index in range(112):
  offset = 0x6b87ab0 + index * 0x78
  id, name, icon = text(offset), text(offset + 16), text(offset + 32)
  assert re.fullmatch('[a-z][a-z0-9_]+', id) and id not in spell_rows
  pointer, count = struct.unpack_from('<QQ', data, offset + 48)
  allowed = list(data[raw(pointer):raw(pointer) + count]) if count else []
  assert all(kind in range(5) for kind in allowed)
  words = struct.unpack_from('<14I', data, offset + 64); floats = struct.unpack_from('<14f', data, offset + 64)
  cast = words[13] >> 8 & 255; school = words[13] & 255
  assert cast in CAST_HINTS and school in range(7)
  record = dict(id=id, name=name, icon=icon, sourceOffset=hex(offset),
      school=SCHOOLS[school], castKindId=cast, castDamageScale=struct.unpack_from('<f', data, 0x7186414 + (cast - 2) * 4)[0] if 2 <= cast <= 8 else 1.0,
      baseDamage=floats[1], baseCooldown=floats[2], range=floats[3], radius=floats[4], targets=words[5],
      duration=floats[6], ticks=words[7], projectileSpeed=floats[8], statusKind=words[10], statusValue=floats[11], statusDuration=floats[12],
      allowedUpgrades=allowed, upgrades=[], sourceSpellId=words[0], sourcePackedBehaviorWord=hex(words[13]),
      sourceSchoolDiscriminant=school, sourceCastDiscriminant=cast, sourceBehaviorUpperBits=words[13] >> 16,
      sourceBehaviorKindHint=CAST_HINTS[cast], sourceTargetingWord=hex(words[9]))
  record['statusKindName']=STATUS_HINTS[words[10]]
  if id in PASSIVE_TEXTS:
   begin,end=PASSIVE_TEXTS[id];a=data.find(begin.encode(),0x7185200,0x7185c80);assert a>=0
   z=data.find(end.encode(),a,0x7185c80);assert z>=0;z+=len(end.encode())
   record['sourcePassive']=dict(kind=id,descriptionTemplate=data[a:z].decode('utf8'),descriptionProvenance=ref(a,z-a),
       cooldownFloorBeforeHaste=PASSIVE_FLOORS[id],cooldownFloorEvidence='Native VA1458865B0..1458866C3 compares exact source IDs and returns floor; common effectiveCD=max(floor,max(.25,baseCD*1.4*rankCDfactor*talentCDfactor*globalCDfactor))/max(.1,spellStatusModifier)',
       runtimeAdapterRequired=True)
  record['upgrades']=[dict(id=str(kind),modifier=MODIFIERS[kind],value=old['upgradeRules'][MODIFIERS[kind]+'Rarity'][0],
      rarityValues=old['upgradeRules'][MODIFIERS[kind]+'Rarity'],
      sourceEvidence=f"Original allowed-upgrade byte {kind} in source ability row {hex(offset)}, slice {hex(raw(pointer))}; common value and all5 tiers from shared original GetUpgradeValue VA1458978C0; {old['upgradeRules']['sourceEvidence']}",
      description=f"Original {MODIFIERS[kind]} upgrade; value is common-tier first pick, repetition/caps use source upgradeRules") for kind in allowed]
  if id in old_spells:
   existing = copy.deepcopy(old_spells[id]); existing.update(record); record = existing
  else:
   # The coordinator owns adapting the native dispatch; a hint is not a claim
   # that an existing Mage behavior implements every other class ability.
   record.update(kind=CAST_HINTS[cast], targeting='sourceBehaviorPending', sourceBehaviorEvidence='Source numeric enum/table verified; full dispatch semantics require native branch evidence')
  spell_rows[id] = record
  raw_spells[id] = dict(**ref(offset,120), allowedUpgradeKindsSource=ref(raw(pointer),count) if count else None,
      words=[hex(v) for v in words], finiteFloats=[v if math.isfinite(v) else None for v in floats])

 for index, expected_id in enumerate(CLASS_IDS):
  offset = 0x6b867c0 + index * 0x90; id = text(offset); assert id == expected_id
  abilities = string_slice(offset + 32); assert len(abilities) == len(set(abilities)) and all(s in spell_rows for s in abilities)
  starter = text(offset + 48); assert starter in abilities
  wow_id = data[offset + 124]
  definition = dict(id=id,name=text(offset+16),startingSpell=starter,color=text(offset+64),icon=text(offset+80),description=text(offset+96),
      sourceOffset=hex(offset),sourceWowClassId=wow_id,spellIds=abilities,talentTrees=[])
  hp,damage_reduction,parameter_88=struct.unpack_from('<3f',data,offset+112)
  weapon_offset=offset-16
  has_auto_weapon=struct.unpack_from('<I',data,weapon_offset)[0]
  assert has_auto_weapon in [0,1]
  definition.update(sourceBaseHealth=hp,sourceBaseDamageReduction=damage_reduction,sourceParameter88=parameter_88,
      sourceBaseStatsEvidence='Native stats rebuild VA14590AAC5 loads class native+0x84 = base damage reduction, and14590ACB9 loads native+0x80 = baseHP; native+0x88 retained as unresolved raw parameter, not claimed as regeneration.',
      sourceAutoWeapon=dict(enabled=bool(has_auto_weapon),damage=struct.unpack_from('<f',data,weapon_offset+4)[0] if has_auto_weapon else 0,
          cooldown=struct.unpack_from('<f',data,weapon_offset+8)[0] if has_auto_weapon else 0,range=struct.unpack_from('<f',data,weapon_offset+12)[0] if has_auto_weapon else 0,
          provenance=ref(weapon_offset,16),sourceEvidence='Original class native pointer = visible row -0x10; original auto-weapon optional struct is the16-byte PREFIX at visible-0x10. Separate source auto-attack consumer VA145913D20..145913E75 applies SourceStat16 only to this struct; present for Warrior,Paladin,Rogue. Hunter AutoShot is an ordinary spell, not this optional melee weapon.'))
  raw_classes.append(dict(id=id,**ref(offset-16,144),visibleRowRawOffset=hex(offset),sourceParameterFloats=list(struct.unpack_from('<3f',data,offset+112)),
      sourceTailWords=[hex(v) for v in struct.unpack_from('<4I',data,offset+112)]))
  talent_rows = []; trees = []
  for tree_index in range(3):
   descriptor = 0x717d018 + index * 0xc8 + tree_index * 64
   tree_name, tree_id, tree_icon = text(descriptor),text(descriptor+16),text(descriptor+32)
   pointer,count = struct.unpack_from('<QQ',data,descriptor+48); start=raw(pointer)
   tree = dict(id=tree_id,name=tree_name,icon=tree_icon,sourceOffset=hex(descriptor),sourceNodesOffset=hex(start),sourceNodeCount=count)
   trees.append(tree); definition['talentTrees'].append(tree)
   for n in range(count):
    row=start+n*104; effects_pointer,effect_count=struct.unpack_from('<QQ',data,row+48); effects=[]
    for k in range(effect_count):
     effect_offset=raw(effects_pointer)+k*64; words=struct.unpack_from('<16I',data,effect_offset);f=struct.unpack_from('<16f',data,effect_offset)
     packed=words[0]; tag=packed&255
     effect=dict(sourceOffset=hex(effect_offset),tag=tag,rawPackedTag=packed,packedFlags=packed>>8,rawWords=[hex(v) for v in words])
     if tag==30:effect.update(kind='stat',stat=words[2],value=f[1],statName=STAT_HINTS.get(words[2],f'sourceStat{words[2]}'))
     elif tag==31:effect.update(kind='spellModifier',value=f[8],modifier=words[9],**filt(effect_offset+8))
     elif tag==0:effect.update(kind='castProc',chance=f[1],internalCooldown=f[2],spell=text(effect_offset+16),
         repeatTriggeringSpell=not bool(text(effect_offset+16)),triggerEvent=words[8],triggerEventName=EVENT_HINTS.get(words[8],f'sourceEvent{words[8]}'),eventValue=f[9],**filt(effect_offset+40))
     elif tag==1:effect.update(kind='criticalCounterCast',count=words[1],maxPerSecond=words[2],spell=text(effect_offset+16),
         triggerEvent=words[8],triggerEventName=EVENT_HINTS.get(words[8],f'sourceEvent{words[8]}'),**filt(effect_offset+40))
     elif tag==2:effect.update(kind='killSummon',chance=f[1],duration=f[2],maxPets=words[3],triggerEvent=words[6],triggerEventName=EVENT_HINTS.get(words[6],f'sourceEvent{words[6]}'),**pet_fields(effect_offset,16))
     elif tag==3:effect.update(kind='shieldBreakBurst',school=SCHOOLS[packed>>8&255],value=f[1],radius=f[2],spellId=words[3],maxPerSecond=words[4])
     elif tag==4:effect.update(kind='impactSplash',value=f[1],radius=f[2],**filt(effect_offset+32))
     elif tag==5:effect.update(kind='targetMaxHealthDamage',value=f[1],**filt(effect_offset+16))
     elif tag==6:effect.update(kind='chain',targets=words[1],damageFactor=f[2],radius=10.0,sourceRadiusEvidence='VA14592FC05 loads raw5d4b760 f32=10; VA145931AB3 passes radius to target selection145964310',**filt(effect_offset+24))
     elif tag==7:effect.update(kind='repeatedHit',chance=f[1],count=words[2],damageFactor=f[3],**filt(effect_offset+24))
     elif tag==8:
      status=struct.unpack_from('<i',data,effect_offset+8)[0]
      effect.update(kind='hitStatus',chance=f[1],status=status,statusValue=f[3],statusDuration=f[4],
          duration=f[3] if status in [2,3,4] else f[4],triggerEvent=words[6],**filt(effect_offset+32))
     elif tag==9:effect.update(kind='critDot',spellId=words[1],value=f[2],duration=f[3],triggerEvent=words[4],
         ticks=max(1,math.floor(f[3]+0.5)),interval=f[3]/max(1,math.floor(f[3]+0.5)),
         sourceDotEvidence='VA1459301CD event matcher consumes raw+16;145931CF0 roundf(duration) max1;145932A94 adds remaining same-effect damage;145884032..049 stores totalDamage/N and duration/N',**filt(effect_offset+24))
     elif tag==10:effect.update(kind='killCooldownReset',interval=f[1],**filt(effect_offset+8))
     elif tag==11:effect.update(kind='killExplosion',value=f[1],radius=f[2],spellId=words[3],maxPerSecond=words[4],
         triggerOnAfflictedDeath=bool(packed>>8&255),deathCondition=packed>>8&255,**filt(effect_offset+24))
     elif tag==14:effect.update(kind='spreadDotOnDeath',targets=words[1],**filt(effect_offset+8))
     elif tag==15:effect.update(kind='triggeredTimedStatBuff',stat=packed>>8&255,value=f[1],count=words[2],duration=f[3],
         triggerEvent=words[4],triggerEventName=EVENT_HINTS.get(words[4],f'sourceEvent{words[4]}'),**filt(effect_offset+24))
     elif tag==17:effect.update(kind='triggeredHeal',value=f[1],interval=f[2],triggerEvent=words[4],triggerEventName=EVENT_HINTS.get(words[4],f'sourceEvent{words[4]}'),**filt(effect_offset+24))
     elif tag==18:effect.update(kind='triggeredShield',value=f[1],cap=f[2],triggerEvent=words[4],triggerEventName=EVENT_HINTS.get(words[4],f'sourceEvent{words[4]}'),**filt(effect_offset+24))
     elif tag==19:effect.update(kind='cheatDeath',interval=f[1],value=f[2],duration=f[3])
     elif tag==20:effect.update(kind='overhealShield',cap=f[1])
     elif tag==21:effect.update(kind='healDamageNova',damageFactor=f[1],radius=f[2],school='Nature')
     elif tag==23:effect.update(kind='chainNoFalloff')
     elif tag==24:effect.update(kind='cascade',maxAdditional=words[1])
     elif tag==25:effect.update(kind='shapeshift',spellId=words[1],sourceEvidence='Source effect25 switch RVA5907119: Bear9634 -> display2281 (Tauren2289); Cat768 -> display892 (Tauren8571); Shadow15473 retains hero model')
     elif tag==26:
      periodic_spell=text(effect_offset+8)
      effect.update(kind='periodicCast',interval=f[1],spell=periodic_spell,periodicBaseCooldown=spell_rows[periodic_spell]['baseCooldown'],
          periodicSpellSchool=spell_rows[periodic_spell]['school'],periodicSpellSourceOffset=spell_rows[periodic_spell]['sourceOffset'])
     elif tag in [27,28]:effect.update(kind='startingSpell',spell=text(effect_offset+8))
     elif tag==29:effect.update(kind='summon',**pet_fields(effect_offset,8))
     else:effect.update(kind=f'sourceEffect{tag}',semanticStatus='pendingExactNativeBranchEvidence')
     if hex(effect_offset) in proven_talent_effects:
      known=copy.deepcopy(proven_talent_effects[hex(effect_offset)]);known.update(effect);effect=known
     effect['sourceSemanticEvidence']=dict(level='originalCatalogAndDescription',description=text(row+64),
         note='Exact source enum/payload and original description are exported; cross-engine runtime implementation and event timing must be verified separately. packedFlags is preserved legacy raw upper bytes, not a universal bitfield.')
     effect['rankScaleFields'] = {0:['chance'],2:['chance'],3:['value'],4:['value'],5:['value'],6:['damageFactor'],7:['chance'],8:['chance'],9:['value'],11:['value'],15:['value'],17:['value'],18:['value'],30:['value'],31:['value']}.get(tag,[])
     if tag==8 and effect['status']==-1:effect['rankScaleFields']=['statusValue']
     effect['rankScaleEvidence']='Native effect scaler VA1458DB210..1458DB576 and raw7181848 jump table: selected amount/chance only; radius/duration/cap/count/event remain fixed. Stat30/modifier31 scaled by outer collector.'
     evidence=dict(**ref(effect_offset,64),classId=id,talentId=text(row),treeId=tree_id,rawPackedTag=packed,tag=tag,packedFlags=packed>>8,
         words=[hex(v) for v in words],finiteFloats=[v if math.isfinite(v) else None for v in f])
     all_effects.append(evidence);effects.append(effect)
    tier,column,max_rank=data[row+96:row+99]
    talent=dict(id=text(row),name=text(row+16),icon=text(row+32),description=text(row+64),prerequisite=text(row+80),
        tree=tree_name,treeId=tree_id,tier=tier,column=column,maxRank=max_rank,requiredTreePoints=tier*5,sourceOffset=hex(row),effects=effects)
    if id=='druid' and talent['id'] in ['leader_of_pack','cat_form']:
     talent['excludes']=['cat_form' if talent['id']=='leader_of_pack' else 'leader_of_pack']
     talent['exclusionEvidence']='Original source descriptions explicitly forbid simultaneously learning Dire Bear Form and Cat Form.'
    assert 0<=tier<=6 and 0<=column<=3 and 1<=max_rank<=5
    raw_talents[f'{id}:{talent["id"]}']=dict(**ref(row,104),treeDescriptorSource=ref(descriptor,64),
        effectsSlice=dict(rawOffset=hex(raw(effects_pointer)),count=effect_count,stride=64))
    talent_rows.append(talent)
  assert len({t['id'] for t in talent_rows})==len(talent_rows)
  abilities_catalog=copy.deepcopy(old)
  abilities_catalog.update(classId=id,startingSpell=starter,spells=[spell_rows[s] for s in abilities],
      sourceVersion='v0.102.3 (f78e6fab)',sourceClass=definition,
      unsupported=['Source numeric/catalog data is complete; non-Mage behavior adapters require exact source branch implementation and native verification'])
  abilities_catalog['evidence'] = f"Original class {id} row {hex(offset)}; spell membership slice visible row+0x20 and starting spell+0x30. Full source static112-row spell table at raw0x6b87ab0 stride0x78; 102 original class spells. " + old['evidence']
  talent_catalog=copy.deepcopy(old_talents)
  talent_catalog.update(classId=id,talents=talent_rows,trees=trees,sourceClassOffset=hex(offset),
      sourceVersion='v0.102.3 (f78e6fab)',unresolvedEffectKinds=sorted({e['kind'] for t in talent_rows for e in t['effects'] if e.get('semanticStatus')}),
      runtimeVerificationStatus='Source export only; no all-class runtime gameplay verification performed by exporter')
  (output/'abilities'/f'{id}.json').write_text(json.dumps(abilities_catalog,indent=2),encoding='utf8')
  (output/'talents'/f'{id}.json').write_text(json.dumps(talent_catalog,indent=2),encoding='utf8')
  classes.append(definition)
 races=[]
 for i in range(8):
  off=0x6b85640+i*88
  bonuses_pointer,bonuses_count=struct.unpack_from('<QQ',data,off+32);assert bonuses_count<20
  bonuses=[]
  for j in range(bonuses_count):
   bonus_off=raw(bonuses_pointer)+j*8;bonus_kind,value=struct.unpack_from('<If',data,bonus_off)
   assert bonus_kind in RACE_BONUS_HINTS
   bonuses.append(dict(kind=bonus_kind,name=RACE_BONUS_HINTS[bonus_kind],value=value,provenance=ref(bonus_off,8)))
  races.append(dict(name=text(off),modelPrefix=text(off+16),racialSpell=text(off+48),sourceRaceId=struct.unpack_from('<I',data,off+80)[0],sourceOffset=hex(off),provenance=ref(off,88),
      bonuses=bonuses,descriptions=string_slice(off+64)))
 all_class_ids=set(s for c in classes for s in c['spellIds'])
 summary=dict(sourceSha256=sha,sourceVersion='v0.102.3 (f78e6fab)',classes=classes,races=races,
     classCount=len(classes),classSpellCount=len(all_class_ids),abilityTableCount=len(spell_rows),talentCount=len(raw_talents),treeCount=27,
     auxiliarySpellIds=sorted(set(spell_rows)-all_class_ids),castKindDiscriminants=sorted({s['castKindId'] for s in spell_rows.values()}),
     statusKindDiscriminants=sorted({s['statusKind'] for s in spell_rows.values()}),
     talentEffectDiscriminants=sorted({e['tag'] for e in all_effects}),
     note='Classes and race choices are separate original registries; no invented named hero roster or assumption that every race/class combination is valid')
 (output/'classes.json').write_text(json.dumps(summary,indent=2),encoding='utf8')
 (output/'auxiliary-spells.json').write_text(json.dumps({s:spell_rows[s] for s in summary['auxiliarySpellIds']},indent=2),encoding='utf8')
 enums=dict(sourceSha256=sha,schools=dict(enumerate(SCHOOLS)),castKinds=CAST_HINTS,statusKinds=dict(enumerate(STATUS_HINTS)),
     triggerEvents=EVENT_HINTS,sourceStats=STAT_HINTS,raceBonuses=RACE_BONUS_HINTS,
     filters={0:'all',1:'spellId',2:'spellIdList',3:'schoolExcludingPets',4:'castKindExcludingPets',5:'originalAutoWeapon',6:'petsOnly'},
     sourceFilterEvidence='VA1459342C0..1459344E9; raw71866d8 first-stage7-entry jump table; raw71866f4 second-stage7-entry jump table. Filter4 compares sourceSpell+0x75 and filter+1. Filter6 requires sourceKind2. Filter3 school excludes sourceKind2. Filter5 requires sourceKind0 and sourceSpellId0.',
     triggerEventEvidence='Exact effect payload at+0x20 (tag0/1),+0x10(tag15/17/18) matched against original descriptions. Spell optional empty means repeat triggering spell. tag0+8=f32 internalCooldownSeconds; tag1+8=u32 maxPerSecond, not+0x20.',
     castDispatchEvidence='VA14595DE6C..14595DEFC; raw7186870 signed-relative12-entry dispatch and raw7186414 cast2..8 damage scaling7float table; full native function RVA595dc00 length0x3fb4',
     talentPayloadNote='Low byte is enum tag. Upper bytes are enum-specific payload (tag3 school,tag11 deathCondition,tag15 stat), not universal flags.',
     talentRankScaling=dict(nativeFunction='VA1458DB210..1458DB576',jumpTableRawOffset='0x7181848',
       scaleAmountAtPlus4=[0,2,3,4,5,7,15,17,18],scaleAmountAtPlus8=[6,9],scaleValueAtPlus4PreserveDeathCondition=[11],
       hitStatus8='Chance(+4) scales when statusKind(+8)!=-1; alternate status -1 scales magnitude(+12) and preserves chance. Current354source rows use ordinary status payload.',
       outerCollectorScale=[30,31],note='Unlisted payload fields preserve exact constants; do not multiply duration/radius/cap/count or event discriminants by rank.'),
     sourceLuck=dict(baseline=0,composition='raceBonus+gearBonus+talentBonus+timedStats',
       rarityFormula='u=1+playerLuck+min(level*0.01,0.30)+extraLuck; weights=[60,25*u,10*u*u,4*u*u*u,u*u*u*u]',
       evidence='Source stats rebuild VA14590A850..14590B8FC; Luck initializes0 and adds raceKind10,gearKind13,SourceStat12 before storing player+0x49c. Rarity source VA145958960..1459589E9 (offer kind0); offer kind1 VA1459588AB..14595892C adds0.5 additionalLuck. Offer kind classification must be checked before applying extra0.5.'),
     sourceAutoWeaponEvidence='Native class table raw71863a0 stores native-class pointers = visible class row-0x10.16-byte prefix contains optional flag+damage+cooldown+range. Warrior27.5/1.6/6; Paladin27/1.8/6; Rogue14/1.2/6; allothers disabled. SourceStat16 consumed only in separate auto-attack path VA145913D20..145913E75.',
     unresolved=['Source class native+0x88 is retained as sourceParameter88; semantic not proven, do not apply as regeneration','Exact native timing/scaling for newly decoded effect families still requires corresponding event-adapter implementation and runtime verification','No Moonkin form effect exists in354 source talent rows; no invented form'])
 (output/'source-semantic-enums.json').write_text(json.dumps(enums,indent=2),encoding='utf8')
 for name,obj in [('classes',raw_classes),('spells',raw_spells),('talents',raw_talents),('effects',all_effects)]:
  (output/'evidence'/f'{name}.json').write_text(json.dumps(obj,indent=2),encoding='utf8')
 print(json.dumps({k:v for k,v in summary.items() if k not in ['classes','races']},indent=2))

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--exe',type=Path,default=Path(r'D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/AzerothSurvivors.exe'))
 parser.add_argument('--repo',type=Path,default=Path(r'D:/Games/Saitan/AzerothMegabonkLab'))
 parser.add_argument('--output',type=Path,required=True,help='Private output directory for source tables and byte provenance; do not publish extracted evidence')
 args=parser.parse_args();export(args.exe,args.repo,args.output)
