"""Validate serialized source effect assets without launching a game."""
import argparse, hashlib, json, math, struct, sys
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

from vfx_paths import arguments


def main():
    args = arguments(__doc__)
    from mpq_source import Archives
    from blp_decode import decode_blp
    assets = args.work/'assets'
    manifest = json.loads((assets/'manifest.json').read_text())
    source_catalog = json.loads((args.work/'source-vfx.json').read_text())
    source = Archives()
    checks, failures, models, textures = 0, [], {}, {}

    def check(test, detail):
        nonlocal checks
        checks += 1
        if not test:
            failures.append(detail)

    def finite(value):
        if isinstance(value, float):
            return math.isfinite(value)
        if isinstance(value, list):
            return all(finite(v) for v in value)
        if isinstance(value, dict):
            return all(finite(v) for v in value.values())
        return True

    check(manifest['format'] == 'azeroth-spell-vfx-v1', 'Runtime manifest format changed')
    class_rows = [s for s in manifest['spells'] if s['classId'] != 'auxiliary']
    check(len(class_rows) == 102, 'Missing class spell IDs')
    check(len(manifest['spells']) == 112, 'Missing auxiliary source spell IDs')
    for entry in manifest['textures']:
        key = entry['key']
        textures[key] = entry
        data, archive = source.read(entry['path'])
        check(hashlib.sha256(data).hexdigest() == entry['sha256'], key+' source texture hash')
        expected = decode_blp(data).transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
        raw = (assets/(key+'.rgba')).read_bytes()
        check(raw == expected, key+' raw texture differs from original BLP RGBA/alpha')
        check(len(raw) == entry['width']*entry['height']*4, key+' raw texture length')
        check(Image.open(assets/(key+'.png')).convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes() == raw, key+' PNG/RGBA orientation')

    for spell in manifest['spells']:
        for effect in spell['effects']:
            check(effect.get('slot') is None or 3 <= effect['slot'] <= 12, spell['id']+' excludes sound/shake fields13/14')
            key = effect['key']
            path = assets/(key+'.json')
            check(path.exists(), spell['id']+' missing effect '+key)
            if key in models or not path.exists():
                continue
            model = json.loads(path.read_text())
            models[key] = model
            data, archive = source.read(model['source'])
            check(hashlib.sha256(data).hexdigest() == model['sha256'], key+' source M2 hash')
            check(data[:4] == b'MD20' and struct.unpack_from('<I', data, 4)[0] == 256, key+' source M2 version')
            check(finite(model), key+' contains nonfinite curve/geometry values')
            poses = model.get('bonePoses')
            if poses:
                bone_data = (assets/poses['file']).read_bytes()
                check(len(bone_data) == poses['frameCount']*len(poses['bones'])*64, key+' bone frame byte count')
                bone_values = np.frombuffer(bone_data, dtype='<f4')
                check(bool(np.isfinite(bone_values).all()), key+' nonfinite attachment matrix')
                check(all(e['bone'] in poses['bones'] for e in model['emitters']+model['ribbons']), key+' source attachment bone coverage')
            for group in ['emitters', 'ribbons']:
                for node in model[group]:
                    check(node['texture'] in textures, key+' missing '+group+' texture')
            geo = model['geometry']
            if geo:
                count, frame_count = geo['vertexCount'], geo['frameCount']
                check(len(geo['positions']) == count*3 and len(geo['uv']) == count*2, key+' vertex/UV count')
                frame_data = (assets/geo['file']).read_bytes()
                check(len(frame_data) == frame_count*count*3*4, key+' animation frame bytes')
                frames = np.frombuffer(frame_data, dtype='<f4')
                check(bool(np.isfinite(frames).all()), key+' nonfinite baked vertices')
                for node in geo['nodes']:
                    indices = node['triangles']
                    check(len(indices) % 3 == 0 and all(0 <= i < count for i in indices), key+' invalid triangle indices')
                    check(node['texture'] in textures, key+' missing geometry texture')
    missing_usage = defaultdict(list)
    for spell in source_catalog['spells']:
        for effect in spell['effects']:
            if not effect['available']:
                missing_usage[effect['source']].append(dict(id=spell['id'], classId=spell['classId'], stage=effect['stage']))
    empty_models = [dict(key=k, source=m['source']) for k, m in models.items() if not m['emitters'] and not m['ribbons'] and not (m['geometry'] and m['geometry']['nodes'])]
    source_without_links = [dict(id=s['id'], classId=s['classId'], spellId=s['spellId'], visualId=s.get('visualId')) for s in manifest['spells'] if not s['effects']]
    report = dict(passed=not failures, checks=checks, failures=failures, classSpells=len(class_rows), allSpells=len(manifest['spells']),
                  models=len(models), textures=len(textures), sourceAlphaExact=True if not failures else None,
                  missingSourceModels=[dict(path=path, usage=usage) for path, usage in sorted(missing_usage.items())],
                  sourceSpellsWithoutAttachedVfx=source_without_links, modelsWithoutMeshParticleOrRibbon=empty_models,
                  bytes=sum(p.stat().st_size for p in assets.rglob('*') if p.is_file()))
    (args.work/'validation.json').write_text(json.dumps(report, indent=2))

    size, columns = 112, 12
    sheet = Image.new('RGB', (size*columns, size*math.ceil(len(textures)/columns)), '#222222')
    draw = ImageDraw.Draw(sheet)
    for i, entry in enumerate(textures.values()):
        x, y = (i % columns)*size, (i//columns)*size
        for cy in range(0, size-16, 12):
            for cx in range(0, size, 12):
                draw.rectangle((x+cx, y+cy, x+cx+11, y+cy+11), fill=('#555555' if (cx+cy)//12 % 2 else '#888888'))
        image = Image.open(assets/(entry['key']+'.png')).convert('RGBA')
        image.thumbnail((size-8, size-24))
        sheet.paste(image, (x+(size-image.width)//2, y+(size-16-image.height)//2), image)
        draw.text((x+2, y+size-15), entry['path'].split('\\')[-1][:17], fill='white')
    sheet.save(args.work/'source-textures-preview.png')
    print(json.dumps({k: v for k, v in report.items() if k not in ['missingSourceModels', 'sourceSpellsWithoutAttachedVfx']}, indent=2))
    if failures:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
