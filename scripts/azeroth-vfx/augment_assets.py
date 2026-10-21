"""Add source bone attachments and retained material flags to a private VFX pack.

Original MPQs and existing animation vertex buffers are read only.
The installed MD20/v256 arrays, not a later-version header layout, are authority.
"""
from pathlib import Path
import hashlib
import json
import math
import struct
import sys

import numpy as np

from vfx_paths import arguments
args=arguments(__doc__)
ROOT=args.work
from mpq_source import Archives
from convert_m2 import arr, Track, tr, qm

assets = ROOT / 'assets'
source = Archives()
conversion = []
C = np.array([[0., -1., 0., 0.], [0., 0., 1., 0.], [1., 0., 0., 0.], [0., 0., 0., 1.]])


def sha(data):
    return hashlib.sha256(data).hexdigest()


for file in sorted(assets.glob('*.json')):
    model = json.loads(file.read_text())
    if 'source' not in model:
        continue
    raw, archive = source.read(model['source'])
    assert raw[:4] == b'MD20' and struct.unpack_from('<I', raw, 4)[0] == 256
    assert sha(raw) == model['sha256']
    globals_ = [v[0] for v in arr(raw, 20, 'I')]
    sequences = arr(raw, 28, 'HHIIfIhHIII7fhH')
    start, end = sequences[0][2:4] if sequences else (0, 800)
    duration = max(.1, (end-start)/1000)
    bone_count, bone_offset = struct.unpack_from('<II', raw, 52)
    bones = []
    for index in range(bone_count):
        at = bone_offset + index*108
        bones.append(dict(parent=struct.unpack_from('<h', raw, at+8)[0],
                          pivot=np.array(struct.unpack_from('<3f', raw, at+96)),
                          t=Track(raw, at+12, 3, [0, 0, 0]),
                          r=Track(raw, at+40, 4, [0, 0, 0, 1], True),
                          s=Track(raw, at+68, 3, [1, 1, 1])))
    attachment_bones = set()
    embedded = []
    particle_count, particle_offset = struct.unpack_from('<II', raw, 316)
    exported_emitters = []
    for index in range(particle_count):
        at = particle_offset + index*504
        bone, texture = struct.unpack_from('<HH', raw, at+20)
        if texture >= len(model['textures']) or not model['textures'][texture]:
            continue
        node = model['emitters'][len(exported_emitters)]
        assert node['bone'] == bone and node['sourceTextureIndex'] == texture
        node['particleType'], node['tail'], node['rotation'] = struct.unpack_from('<BBH', raw, at+44)
        for label, offset in [('geometryModel', 24), ('recursionModel', 32)]:
            length, address = struct.unpack_from('<II', raw, at+offset)
            value = raw[address:address+length].rstrip(b'\0').decode('utf8') if length else ''
            node[label] = value
            if value:
                embedded.append(dict(index=index, kind=label, path=value))
        assert 0 <= bone < len(bones)
        attachment_bones.add(bone)
        exported_emitters.append(node)
    assert len(exported_emitters) == len(model['emitters'])

    materials = arr(raw, 132, 'HH')
    _, view = struct.unpack_from('<II', raw, 76)
    if model.get('geometry'):
        texture_lookup = arr(raw, 148, 'H')
        exported_nodes = []
        sub = arr(raw, view+24, '10H3f')
        for unit in arr(raw, view+32, '12H'):
            texture = texture_lookup[unit[8]][0] if unit[8] < len(texture_lookup) else -1
            if unit[2] >= len(sub) or not 0 <= texture < len(model['textures']) or not model['textures'][texture]:
                continue
            node = model['geometry']['nodes'][len(exported_nodes)]
            flags, blend = materials[unit[5]] if unit[5] < len(materials) else (0, 2)
            assert node['blend'] == blend and node['texture'] == model['textures'][texture]
            node['materialFlags'] = flags
            exported_nodes.append(node)
        assert len(exported_nodes) == len(model['geometry']['nodes'])

    ribbon_count, ribbon_offset = struct.unpack_from('<II', raw, 308)
    exported_ribbons = []
    for index in range(ribbon_count):
        at = ribbon_offset + index*220
        texture_ids, material_ids = arr(raw, at+20, 'H'), arr(raw, at+28, 'H')
        texture = texture_ids[0][0] if texture_ids else -1
        material = material_ids[0][0] if material_ids else -1
        if not 0 <= texture < len(model['textures']) or not model['textures'][texture]:
            continue
        node = model['ribbons'][len(exported_ribbons)]
        bone = struct.unpack_from('<I', raw, at+4)[0]
        assert 0 <= bone < len(bones)
        node['bone'] = bone
        node['materialFlags'] = materials[material][0] if 0 <= material < len(materials) else 0
        attachment_bones.add(bone)
        exported_ribbons.append(node)
    assert len(exported_ribbons) == len(model['ribbons'])

    row = dict(key=file.stem, source=model['source'], emitters=len(exported_emitters),
               ribbons=len(exported_ribbons), embeddedModels=embedded)
    if attachment_bones:
        selected_bones = sorted(attachment_bones)
        frame_count = model['geometry']['frameCount'] if model.get('geometry') else max(2, int(math.ceil(duration*24))+1)
        frames = []
        for frame in range(frame_count):
            now = start + (end-start)*frame/(frame_count-1)
            cache = {}

            def world(index):
                if index in cache:
                    return cache[index]
                bone = bones[index]
                pv = bone['pivot']
                local = tr(pv) @ tr(bone['t'].sample(now, 0, globals_)) @ qm(bone['r'].sample(now, 0, globals_)) @ np.diag([*bone['s'].sample(now, 0, globals_), 1.]) @ tr(-pv)
                cache[index] = world(bone['parent']) @ local if bone['parent'] >= 0 else local
                return cache[index]

            frames.append([C @ world(index) @ C.T for index in selected_bones])
        frames = np.asarray(frames, dtype='<f4')
        assert np.isfinite(frames).all()
        filename = file.stem+'.bones.bin'
        frames.tofile(assets/filename)
        model['bonePoses'] = dict(file=filename, frameCount=frame_count, bones=selected_bones,
                                  duration=duration, matrixLayout='row-major float32 Unity coordinates')
        row.update(boneCount=len(selected_bones), frameCount=frame_count,
                   boneFileSha256=sha((assets/filename).read_bytes()))
        if 'rainoffire' in model['source'].lower():
            row['emitterPositions'] = []
            for index, emitter in enumerate(exported_emitters):
                poses = frames[:, selected_bones.index(emitter['bone'])]
                positions = np.einsum('fij,j->fi', poses, [*emitter['position'], 1.])[:, :3]
                row['emitterPositions'].append(dict(index=index, bone=emitter['bone'],
                    ignoredLocal=emitter['position'], first=positions[0].tolist(),
                    minimum=positions.min(0).tolist(), maximum=positions.max(0).tolist()))
    file.write_text(json.dumps(model, separators=(',', ':')))
    conversion.append(row)

manifest_file = assets/'manifest.json'
manifest = json.loads(manifest_file.read_text())
manifest['fidelityRevision'] = 'bone-attachments-material-flags-v1'
manifest['limitations'] = [
    'Source geometry and bone attachments are sampled at24Hz in the Unity adapter; source camera billboard bones are not reconstructed.',
    'Particle shape/head-tail/rotation and specialized material behavior require further source-runtime equivalence checks; no exact fidelity claim.',
    'Missing original DBC models remain reported and omitted.'
]
manifest_file.write_text(json.dumps(manifest, indent=2))
(ROOT/'asset-augmentation.json').write_text(json.dumps(dict(models=len(conversion), details=conversion), indent=2))
print(json.dumps(dict(models=len(conversion), attachmentModels=sum('boneCount' in x for x in conversion),
                      emitters=sum(x['emitters'] for x in conversion), ribbons=sum(x['ribbons'] for x in conversion),
                      embeddedModels=sum(len(x['embeddedModels']) for x in conversion))))
