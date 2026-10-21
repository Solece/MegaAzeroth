"""Rebuild the private four-pass DruidCat material profile without changing geometry."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import sys

SOURCE_SHA256 = '2e48a967e14b4258e0d598b474019b6545ed2233acad567f76bc20e522915f77'
PROFILE_SHA256 = 'ec7355ca6b98b126949ec0e00c65c8c511288e87b587625df08e910296af9b1c'
SOURCE_MODEL = 'Creature\\DruidCat\\DruidCat.m2'
EXPECTED_PASSES = [(0, 0, 670), (4, 1, 60), (1, 0, 8), (17, 2, 4)]


def digest(data):
    return hashlib.sha256(data).hexdigest()


def table(data, position, fields):
    count, offset = struct.unpack_from('<II', data, position)
    size = struct.calcsize('<' + fields)
    if count and offset + count * size > len(data):
        raise ValueError('Original M2 table exceeds source data')
    return [struct.unpack_from('<' + fields, data, offset + i * size) for i in range(count)]


def export(pack, destination):
    pack = pack.resolve(strict=True)
    destination = destination.resolve()
    protected = {path.resolve() for path in pack.iterdir() if path.is_file() and path.name != 'source-material-profile.json'}
    if destination in protected:
        raise ValueError('Output would overwrite an existing source/geometry/texture input')
    tracked = [pack / name for name in ['model.json', 'conversion.json', 'original.m2', 'texture.rgba', 'texture.rgba.json']]
    tracked += sorted(pack.glob('*.vertices.bin'))
    before = {path: digest(path.read_bytes()) for path in tracked}
    source = (pack / 'original.m2').read_bytes()
    model = json.loads((pack / 'model.json').read_text(encoding='utf-8'))
    conversion = json.loads((pack / 'conversion.json').read_text(encoding='utf-8'))
    checks = 0

    def require(condition, message):
        nonlocal checks
        if not condition:
            raise ValueError(message)
        checks += 1

    require(source[:4] == b'MD20' and struct.unpack_from('<I', source, 4)[0] == 256, 'Unsupported original M2 format')
    require(digest(source) == SOURCE_SHA256 == model['source']['sha256'], 'Original Cat source SHA256 differs from the verified model')
    require(model['source']['model'] == SOURCE_MODEL and model['source']['displayId'] == 892, 'Only original canonical DruidCat display892 is supported')
    require(model['name'] == conversion['name'] == 'DruidCat', 'Pack/catalog is not DruidCat')
    require(model['vertexCount'] == conversion['vertices'] == 603, 'Unexpected Cat vertex count')
    require(len(model['positions']) == len(model['normals']) == 603 * 3 and len(model['uv']) == 603 * 2, 'Incomplete mesh attributes')
    require(all(math.isfinite(value) for key in ['positions', 'normals', 'uv'] for value in model[key]), 'Nonfinite geometry/UV')
    views, view = struct.unpack_from('<II', source, 76)
    require(views > 0, 'No original LOD0 skin view')
    submeshes = table(source, view + 24, '10H3f')
    units = table(source, view + 32, '12H')
    materials = table(source, 132, 'HH')
    texture_lookup = table(source, 148, 'H')
    lookup = table(source, view, 'H')
    native_indices = [lookup[index[0]][0] for index in table(source, view + 8, 'H')]
    source_vertices = model['source']['sourceVertexIndices']
    require(len(source_vertices) == 603, 'Missing source-to-runtime vertex map')
    require(conversion['submeshes'] == [list(row) for row in submeshes], 'Conversion submesh provenance differs from original M2')
    require(conversion['textureUnits'] == [list(row) for row in units], 'Conversion texture-unit provenance differs from original M2')
    require(len(submeshes) == len(units) == 4, 'Expected four original source draw passes')
    indices = model['triangles']
    require(len(indices) == 2226 and conversion['triangles'] == 742, 'Source/runtime triangle count mismatch')
    require(all(isinstance(index, int) and 0 <= index < 603 for index in indices), 'Invalid runtime triangle index')
    texture_info = json.loads((pack / 'texture.rgba.json').read_text(encoding='utf-8'))
    width, height = texture_info['width'], texture_info['height']
    rgba = (pack / 'texture.rgba').read_bytes()
    require((width, height) == (304, 264) and len(rgba) == width * height * 4, 'Unexpected original Cat atlas dimensions')
    passes, alpha_checks = [], []
    offset = 0
    for pass_index, (submesh, unit, expected) in enumerate(zip(submeshes, units, EXPECTED_PASSES)):
        flags, blend = materials[unit[5]]
        count = submesh[5]
        require(unit[2] == pass_index and submesh[4] == offset, 'Original draw-range order is not contiguous')
        require((flags, blend, count // 3) == expected and count % 3 == 0, 'Original per-pass material/triangle classification differs')
        triangles = indices[offset:offset + count]
        native = native_indices[submesh[4]:submesh[4] + count]
        reversed_native = [native[i + order] for i in range(0, count, 3) for order in [0, 2, 1]]
        require([source_vertices[index] for index in triangles] == reversed_native,
                'Runtime/source partition or reflected triangle winding differs in pass' + str(pass_index))
        alphas = []
        for vertex in sorted(set(triangles)):
            u, v = model['uv'][vertex * 2:vertex * 2 + 2]
            x, y = min(width - 1, max(0, int(u * width))), min(height - 1, max(0, int(v * height)))
            alphas.append(rgba[(y * width + x) * 4 + 3])
        alpha_checks.append({'sourceSubmesh': pass_index, 'sourceBlend': blend,
                             'uniqueVertexUvAlphaBelow128': sum(a < 128 for a in alphas),
                             'alphaMin': min(alphas), 'alphaMax': max(alphas)})
        passes.append({'sourceSubmesh': pass_index, 'sourceMaterialIndex': unit[5], 'flags': flags, 'blend': blend,
                       'textureId': texture_lookup[unit[8]][0], 'triangles': triangles})
        offset += count
    require(offset == len(indices), 'Source profile omitted runtime triangles')
    # Texture alpha is meaningful only under the matching native source blend:
    # body low-alpha texels must remain opaque, while fur needs alpha testing.
    require(alpha_checks[0]['alphaMin'] < 128 and passes[0]['blend'] == 0,
            'Opaque-body alpha regression classification failed')
    require(alpha_checks[1]['alphaMin'] < 128 < alpha_checks[1]['alphaMax'] and passes[1]['blend'] == 1,
            'Two-sided cutout-fur alpha classification failed')
    require(passes[2]['blend'] == 0 and passes[3]['blend'] == 2,
            'Opaque teeth / alpha-blended eyes classification failed')
    profile = {'format': 'azeroth-creature-material-profile-v1', 'sourceSha256': SOURCE_SHA256,
               'sourceDisplayId': 892, 'texture': 'texture.rgba', 'passes': passes}
    encoded = json.dumps(profile, separators=(',', ':')).encode('utf-8')
    require(digest(encoded) == PROFILE_SHA256, 'Generated profile differs from the exact source-verified reference')
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(encoded)
    require(all(digest(path.read_bytes()) == value for path, value in before.items()), 'Geometry/source/texture input changed')
    return {'passed': True, 'checks': checks, 'sourceDisplayId': 892, 'profileSha256': PROFILE_SHA256,
            'triangleCounts': [len(p['triangles']) // 3 for p in passes],
            'sourceFlags': [p['flags'] for p in passes], 'sourceBlends': [p['blend'] for p in passes],
            'globalMaterialMode': model.get('materialMode'), 'alphaClassification': alpha_checks,
            'geometryInputsUnchanged': True, 'output': str(destination), 'nativeGameplayVerified': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pack', type=Path, required=True, help='Privately generated original Cat pack directory')
    parser.add_argument('--output', type=Path, help='Default: PACK/source-material-profile.json')
    args = parser.parse_args()
    print(json.dumps(export(args.pack, args.output or args.pack / 'source-material-profile.json')))
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        raise SystemExit(1)
