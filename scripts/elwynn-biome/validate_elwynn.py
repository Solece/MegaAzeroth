"""Check converted Elwynn bytes, geometry, UVs and every original render pass."""
from pathlib import Path
import hashlib, json, struct, sys
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/azeroth-assets'))
sys.path.insert(0, str(ROOT / 'scripts/azeroth-mage'))
from mpq_source import Archives
from blp_decode import decode_blp
from build_elwynn import array, unity
from wmo_validate import validate_buildings

def validate(folder):
 manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf8'))
 assert manifest['format'] in ['azeroth-elwynn-biome-v1', 'azeroth-elwynn-biome-v2']
 assert len(manifest['groundTextures']) == 6 and len(manifest['props']) == 15
 source = Archives(); stats = dict(sourceFiles=0, textures=0, props=0, passes=0, triangles=0, cutoutPasses=0)
 for item in manifest['sourceEvidence']:
  raw, archive = source.read(item['path'])
  assert archive == item['archive'] and len(raw) == item['bytes']
  assert hashlib.sha256(raw).hexdigest() == item['sha256']
  stats['sourceFiles'] += 1
 for key, texture in manifest['textures'].items():
  raw = (folder / texture['file']).read_bytes()
  assert len(raw) == texture['width'] * texture['height'] * 4
  assert hashlib.sha256(raw).hexdigest() == texture['rawSha256']
  original, _ = source.read(texture['source']['path'])
  image = decode_blp(original).convert('RGBA')
  assert image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes() == raw
  alpha = np.asarray(image)[:, :, 3]
  assert texture['alphaMin'] == int(alpha.min()) and texture['alphaMax'] == int(alpha.max())
  assert texture['alphaNonOpaque'] == int(np.count_nonzero(alpha < 255))
  stats['textures'] += 1
 for key, prop in manifest['props'].items():
  model = json.loads((folder / prop['file']).read_text(encoding='utf8'))
  raw, _ = source.read(model['source']['path'])
  rows = array(raw, 68, '3f4B4B3f2f2f'); count = len(rows)
  expected_positions = unity([r[:3] for r in rows])
  expected_normals = unity([r[11:14] for r in rows])
  expected_uv = np.asarray([r[14:16] for r in rows], dtype=np.float32); expected_uv[:, 1] = 1 - expected_uv[:, 1]
  for name, expected in [('positions', expected_positions), ('normals', expected_normals), ('uv', expected_uv)]:
   actual = np.asarray(model[name]).reshape(expected.shape)
   assert np.isfinite(actual).all() and np.array_equal(actual, expected), (key, name)
  assert np.array_equal(model['boundsMin'], expected_positions.min(0))
  assert np.array_equal(model['boundsMax'], expected_positions.max(0))
  view = struct.unpack_from('<I', raw, 80)[0]
  lookup = array(raw, view, 'H'); indices = [lookup[x[0]][0] for x in array(raw, view + 8, 'H')]
  sections = array(raw, view + 24, '10H3f'); units = array(raw, view + 32, '12H')
  flags = array(raw, 132, '2H'); texturelookup = array(raw, 148, 'H')
  assert len(model['passes']) == len(units)
  for p, unit in zip(model['passes'], units):
   section = sections[unit[2]]
   expected = np.asarray(indices[section[4]:section[4] + section[5]]).reshape(-1, 3)[:, [0, 2, 1]]
   assert np.array_equal(p['triangles'], expected.reshape(-1))
   assert p['renderFlags'] == flags[unit[5]][0] and p['blendMode'] == flags[unit[5]][1]
   assert p['texture'] == model['source']['sourceTextureSlots'][texturelookup[unit[8]][0]]
   assert max(p['triangles']) < count
   if p['blendMode'] == 1:
    assert manifest['textures'][p['texture']]['alphaNonOpaque'] > 0, key
    stats['cutoutPasses'] += 1
   stats['passes'] += 1; stats['triangles'] += len(p['triangles']) // 3
  stats['props'] += 1
 stats.update(validate_buildings(folder, manifest, source, unity))
 report = dict(status='PASS', **stats, allSourceHashesMatch=True,
     allOriginalPositionsNormalsUvsMatch=True, allRenderPassesMatch=True,
     rgbaOrientationVerified=True, alphaKeyPixelsPreserved=True,
     wmoOriginalGeometryAndCollisionMatch=True, wmoDoorwaysUseActualOpenings=True)
 (folder / 'validation.json').write_text(json.dumps(report, indent=2), encoding='utf8')
 print(json.dumps(report, indent=2))

if __name__ == '__main__':
 validate(Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'private-assets/elwynn-biome')
