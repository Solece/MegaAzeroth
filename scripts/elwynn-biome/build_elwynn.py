"""Build the private Elwynn biome pack from the user's installed Azeroth archives.

No game assets are committed. Vanilla MD20 v256 geometry uses its first skin
profile, original material/texture passes, alpha flags, UVs and source units.
Campfire particles and tree wind are deliberately outside this static pack.
"""
from pathlib import Path
import argparse, hashlib, json, struct, sys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/azeroth-assets'))
sys.path.insert(0, str(ROOT / 'scripts/azeroth-mage'))
from mpq_source import Archives
from blp_decode import decode_blp

GROUNDS = {
 'grass': r'Tileset\Elwynn\ElwynnGrassBase_s.blp',
 'flowers': r'Tileset\Elwynn\ElwynnFlowerBase_s.blp',
 'dirt': r'Tileset\Elwynn\ElwynnDirtBase2_s.blp',
 'rock': r'Tileset\Elwynn\ElwynnRockBaseTest2_s.blp',
 'cobble': r'Tileset\Elwynn\ElwynnCobbleStoneBase_s.blp',
 'crops': r'Tileset\Elwynn\ElwynnCrop92_s.blp',
}
PROPS = [
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTreeCanopy01.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTreeCanopy02.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTreeCanopy03.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTreeCanopy04.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTreeMid01.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTree01\ElwynnPine01.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Trees\ElwynnTree01\ElwynnPine02.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Bush\ElwynnBush09.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\CliffRocks\ElwynnCliffRock01.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\CliffRocks\ElwynnCliffRock02.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Detail\ElwynnRock2\ElwynnRock2.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\ElwynnFences\ElwynnWoodFence01.m2',
 r'World\Azeroth\Elwynn\PassiveDoodads\Campfire\ElwynnCampfire.m2',
 r'World\Azeroth\Westfall\PassiveDoodads\Barrel\WestfallBarrel01.m2',
 r'World\Generic\PassiveDoodads\Barrel\Barrel01.m2',
]

def array(data, offset, fmt):
 count, start = struct.unpack_from('<II', data, offset)
 stride = struct.calcsize('<' + fmt)
 if count and start + count * stride > len(data):
  raise ValueError(f'Out of bounds M2 array at {offset}: {count} * {stride}')
 return [struct.unpack_from('<' + fmt, data, start + i * stride) for i in range(count)]

def unity(values):
 v = np.asarray(values, dtype=np.float32)
 return np.stack([-v[:, 1], v[:, 2], v[:, 0]], axis=1)

def build(out):
 source = Archives(); out.mkdir(parents=True, exist_ok=True)
 (out / 'textures').mkdir(exist_ok=True)
 (out / 'props').mkdir(exist_ok=True)
 textures = {}; ground = {}; props = {}; evidence = {}

 def read(path):
  data, archive = source.read(path)
  item = dict(path=path, archive=archive, bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
  evidence[path.lower()] = item
  return data, item

 def texture(path, flags=0):
  data, item = read(path)
  # Wrap flags belong to the M2 texture slot. Different slots may share pixels
  # while using a different sampler, so preserve both in the runtime key.
  key = item['sha256'][:16] + '-' + str(flags)
  if key not in textures:
   image = decode_blp(data).convert('RGBA')
   raw = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
   file = f'textures/{key}.rgba'
   (out / file).write_bytes(raw)
   image.save(out / 'textures' / (key + '.png'))
   alpha = np.asarray(image)[:, :, 3]
   textures[key] = dict(file=file, width=image.width, height=image.height,
      format='RGBA32', rowOrder='bottom-up', wrapU=bool(flags & 1), wrapV=bool(flags & 2),
      alphaMin=int(alpha.min()), alphaMax=int(alpha.max()),
      alphaNonOpaque=int(np.count_nonzero(alpha < 255)),
      rawSha256=hashlib.sha256(raw).hexdigest(), source=item)
  return key

 for key, path in GROUNDS.items():
  ground[key] = texture(path, 3)

 for path in PROPS:
  data, item = read(path)
  if data[:4] != b'MD20' or struct.unpack_from('<I', data, 4)[0] != 256:
   raise ValueError('Expected installed Vanilla MD20 version256: ' + path)
  rows = array(data, 68, '3f4B4B3f2f2f')
  positions = unity([r[:3] for r in rows]); normals = unity([r[11:14] for r in rows])
  uv = np.asarray([r[14:16] for r in rows], dtype=np.float32); uv[:, 1] = 1 - uv[:, 1]
  count, view = struct.unpack_from('<II', data, 76)
  if not count: raise ValueError('No skin profile: ' + path)
  lookup = array(data, view, 'H')
  indices = [lookup[x[0]][0] for x in array(data, view + 8, 'H')]
  sections = array(data, view + 24, '10H3f')
  units = array(data, view + 32, '12H')
  # Vanilla header contains an extra unknown array at108; material/render
  # flags are at132, NOT124 (124 is replaceable texture lookup).
  materials = array(data, 132, '2H')
  texture_lookup = array(data, 148, 'H')
  texture_meta = array(data, 92, '4I')
  slots = []
  for kind, flags, length, start in texture_meta:
   if kind != 0: raise ValueError(f'Unsupported replaceable environment texture {kind}: {path}')
   texpath = data[start:start + length].rstrip(b'\0').decode('utf8')
   slots.append(texture(texpath, flags))
  passes = []
  for index, unit in enumerate(units):
   section = sections[unit[2]]; flags, blend = materials[unit[5]]
   if unit[7] != 1: raise ValueError('Multitexture shader requires explicit support: ' + path)
   triangles = np.asarray(indices[section[4]:section[4] + section[5]], dtype=np.int32).reshape(-1, 3)[:, [0, 2, 1]]
   if np.max(triangles) >= len(positions): raise ValueError('Invalid source triangle: ' + path)
   passes.append(dict(texture=slots[texture_lookup[unit[8]][0]],
       triangles=triangles.reshape(-1).tolist(), renderFlags=flags, blendMode=blend,
       sourceSection=unit[2], sourceUnit=index, layer=unit[6], doubleSided=bool(flags & 4),
       alphaCutoff=0.5 if blend == 1 else 0.0))
  key = Path(path.replace('\\', '/')).stem.lower()
  model = dict(format='azeroth-elwynn-prop-v1', id=key, vertexCount=len(rows),
      positions=positions.reshape(-1).tolist(), normals=normals.reshape(-1).tolist(), uv=uv.reshape(-1).tolist(),
      passes=passes, boundsMin=positions.min(0).tolist(), boundsMax=positions.max(0).tolist(),
      source=dict(**item, m2Version=256, skinProfile=0, sourceVertexCount=len(rows),
          sourceSectionCount=len(sections), sourceUnitCount=len(units), sourceTextureSlots=slots,
          staticGeometry=True, skippedAnimation='tree wind and source particles are not emitted'))
  file = 'props/' + key + '.json'
  (out / file).write_text(json.dumps(model, separators=(',', ':')), encoding='utf8')
  props[key] = dict(file=file, vertexCount=len(rows), passCount=len(passes),
      triangleCount=sum(len(p['triangles']) // 3 for p in passes), boundsMin=model['boundsMin'], boundsMax=model['boundsMax'])
 manifest = dict(format='azeroth-elwynn-biome-v1', version=1,
     coordinateSystem='Unity left-handed Y-up: source(x,y,z)->(-y,z,x); V flipped; reversed winding; source units',
     origin='Installed Azeroth Survivors archives, read-only source; local pack only',
     groundTextures=ground, textures=textures, props=props, sourceEvidence=list(evidence.values()),
     limits=['Static M2 environment only; original WMO buildings and source particles are not included',
       'No game files are distributed; run this converter against the locally owned installation'])
 (out / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf8')
 print(json.dumps(dict(status='PASS', output=str(out), groundCount=len(ground), propCount=len(props),
     textureCount=len(textures), totalVertices=sum(p['vertexCount'] for p in props.values()),
     totalTriangles=sum(p['triangleCount'] for p in props.values()), props=props), indent=2))

if __name__ == '__main__':
 parser = argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--output', type=Path, default=ROOT / 'private-assets/elwynn-biome')
 args = parser.parse_args(); build(args.output)
