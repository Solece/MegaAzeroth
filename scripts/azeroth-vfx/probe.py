"""Resolve original spell visual assets for all source class catalogs, read-only.

The installed DBC records and MPQ files are the authority. Missing DBC spells
are reported instead of substituting a visually unrelated spell.
"""
import hashlib, json, re, struct
from pathlib import Path

from vfx_paths import arguments


def main():
    args = arguments(__doc__, catalogs=True)
    from mpq_source import Archives
    source = Archives()

    def dbc(name):
        data, archive = source.read('DBFilesClient\\'+name+'.dbc')
        count, fields, size, string_size = struct.unpack_from('<4I', data, 4)
        strings = data[20+count*size:]
        def text(offset):
            return strings[offset:strings.find(b'\0', offset)].decode('utf8', 'replace') if 0 <= offset < len(strings) else ''
        rows = {row[0]: row for row in (struct.unpack_from('<'+'I'*fields, data, 20+i*size) for i in range(count))}
        return rows, text, dict(archive=archive, records=count, fields=fields, size=size, sha256=hashlib.sha256(data).hexdigest())

    spells, _, spell_meta = dbc('Spell')
    visuals, _, visual_meta = dbc('SpellVisual')
    kits, _, kit_meta = dbc('SpellVisualKit')
    effects, effect_text, effect_meta = dbc('SpellVisualEffectName')
    abilities = []
    source_catalogs = []
    class_ids = ['druid', 'hunter', 'mage', 'paladin', 'priest', 'rogue', 'shaman', 'warlock', 'warrior']
    ability_dir = args.catalogs/'abilities' if (args.catalogs/'abilities').is_dir() else args.catalogs
    for path in [ability_dir/(class_id+'.json') for class_id in class_ids]:
        catalog = json.loads(path.read_text(encoding='utf-8-sig'))
        source_catalogs.append(dict(classId=catalog['classId'], path=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest(), count=len(catalog['spells'])))
        for ability in catalog['spells']:
            abilities.append((catalog['classId'], ability))
    auxiliary_path = args.catalogs/'auxiliary-spells.json'
    if auxiliary_path.exists():
        for ability in json.loads(auxiliary_path.read_text(encoding='utf-8-sig')).values():
            abilities.append(('auxiliary', ability))
    assert len(abilities) == 112, 'Expected102 class spells+10 auxiliary source spells'
    assert len({ability['id'] for _, ability in abilities}) == len(abilities), 'Duplicate source ability ID'

    result = []
    models = {}
    for class_id, ability in abilities:
        spell_id = int(ability['sourceSpellId'])
        record = dict(id=ability['id'], classId=class_id, spellId=spell_id, sourceOffset=ability['sourceOffset'], effects=[])
        row = spells.get(spell_id)
        if row is None:
            record.update(error='Original source spell ID not present in installed Spell.dbc', visualId=None)
            result.append(record)
            continue
        visual_id = row[115]
        visual = visuals.get(visual_id)
        record.update(visualId=visual_id, visual=visual)
        links = record['effects']
        if visual:
            if visual[6] and visual[7] in effects:
                links.append(dict(stage='missile', effectId=visual[7], effect=effects[visual[7]], path=effect_text(effects[visual[7]][2])))
            if visual[11] and visual[12] in effects:
                links.append(dict(stage='area', effectId=visual[12], effect=effects[visual[12]], path=effect_text(effects[visual[12]][2])))
            for stage, index in [('precast', 1), ('cast', 2), ('impact', 3), ('channel', 5), ('aura', 4), ('areaKit', 13)]:
                kit = kits.get(visual[index])
                if not kit:
                    continue
                #1.12 fields3..12 are10model attachments;13isSoundID,14isShakeID.
                for slot, effect_id in enumerate(kit[3:13], 3):
                    if effect_id in effects:
                        links.append(dict(stage=stage, slot=slot, kitId=kit[0], effectId=effect_id, effect=effects[effect_id], path=effect_text(effects[effect_id][2])))
        for link in links:
            path = re.sub(r'\.(mdx|mdl)$', '.m2', link['path'], flags=re.I)
            if path not in models:
                try:
                    data, archive = source.read(path)
                    models[path] = dict(available=True, source=path, archive=archive, bytes=len(data), version=struct.unpack_from('<I', data, 4)[0], vertices=struct.unpack_from('<I', data, 68)[0], textures=struct.unpack_from('<I', data, 92)[0], sha256=hashlib.sha256(data).hexdigest())
                except Exception as exc:
                    models[path] = dict(available=False, source=path, error=str(exc))
            link.update(models[path])
        result.append(record)
    output = dict(format='azeroth-all-class-vfx-source-v1', sourceCatalogs=source_catalogs,
                  tables=dict(Spell=spell_meta, SpellVisual=visual_meta, SpellVisualKit=kit_meta, SpellVisualEffectName=effect_meta), spells=result)
    (args.work/'source-vfx.json').write_text(json.dumps(output, indent=2), encoding='utf-8')
    summary = dict(spells=len(result), classSpells=sum(s['classId'] != 'auxiliary' for s in result), classCounts={c['classId']: c['count'] for c in source_catalogs},
                   links=sum(len(s['effects']) for s in result), uniqueModels=len(models), missingSpells=[dict(id=s['id'], spellId=s['spellId'], error=s['error']) for s in result if 'error' in s],
                   missingModels=[dict(path=path, error=model['error']) for path, model in models.items() if not model['available']],
                   spellsWithoutVisuals=[dict(id=s['id'], spellId=s['spellId'], visualId=s.get('visualId')) for s in result if not s['effects'] and 'error' not in s])
    (args.work/'probe-summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
