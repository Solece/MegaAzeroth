"""Independent checks against original WMO chunks and physical doorway rays."""
import hashlib, json, struct
import numpy as np
from wmo_export import chunks

def segment_hits(start, end, vertices, triangles):
 tri = vertices[triangles]; direction = end - start
 edge1 = tri[:, 1] - tri[:, 0]; edge2 = tri[:, 2] - tri[:, 0]
 h = np.cross(np.broadcast_to(direction, edge2.shape), edge2)
 a = (edge1 * h).sum(1); valid = np.abs(a) > 1e-8
 f = np.zeros(len(a)); f[valid] = 1 / a[valid]
 s = start - tri[:, 0]; u = f * (s * h).sum(1)
 q = np.cross(s, edge1); v = f * (q * direction).sum(1); t = f * (edge2 * q).sum(1)
 return np.flatnonzero(valid & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-5) & (t < 1 - 1e-5))

def validate_buildings(folder, manifest, archives, unity):
 stats = dict(buildings=0, wmoGroups=0, wmoVertices=0, wmoRenderTriangles=0, wmoCollisionTriangles=0,
     wmoWalkableTriangles=0, wmoPasses=0, originalEntrances=0, entranceRaysClear=0)
 for id, item in manifest.get('buildings', {}).items():
  building = json.loads((folder / item['file']).read_text(encoding='utf8'))
  original, arc = archives.read(building['source']['path']); root = dict(chunks(original))
  assert building['wmoVersion'] == struct.unpack('<I', root['MVER'])[0] == 17
  assert len(building['groups']) == struct.unpack_from('<I', root['MOHD'], 4)[0]
  all_vertices = []; all_collision = []; offset = 0
  for group in building['groups']:
   raw, arc = archives.read(group['source']['path']); outer = dict(chunks(raw)); data = dict(chunks(outer['MOGP'], 68))
   source_positions = np.frombuffer(data['MOVT'], dtype='<f4').reshape(-1, 3)
   positions = unity(source_positions); normals = unity(np.frombuffer(data['MONR'], dtype='<f4').reshape(-1, 3))
   uv = np.frombuffer(data['MOTV'], dtype='<f4').reshape(-1, 2).copy(); uv[:, 1] = 1 - uv[:, 1]
   for name, expected in [('positions', positions), ('normals', normals), ('uv', uv)]:
    actual = np.asarray(group[name]).reshape(expected.shape)
    assert np.isfinite(actual).all() and np.array_equal(expected, actual), (id, group['id'], name)
   assert np.array_equal(positions.min(0), group['boundsMin']) and np.array_equal(positions.max(0), group['boundsMax'])
   triangles = np.frombuffer(data['MOVI'], dtype='<u2').reshape(-1, 3)[:, [0, 2, 1]]
   mopy = np.frombuffer(data['MOPY'], dtype='u1').reshape(-1, 2)
   assert np.array_equal(mopy[:, 0], group['source']['mopyFlags'])
   assert np.array_equal(mopy[:, 1], group['source']['mopyMaterials'])
   selected = []
   for triangle, (flags, material) in zip(triangles, mopy):
    if flags & 8 or (flags & 32 and not flags & 4): selected.append(triangle)
   selected = np.array(selected, dtype=np.int32)
   assert np.array_equal(selected.reshape(-1), group['collisionTriangles'])
   assert len(group['passes']) == len(data['MOBA']) // 24
   for pass_index, render in enumerate(group['passes']):
    batch = struct.unpack_from('<6hI3H2B', data['MOBA'], pass_index * 24)
    start, count, material = batch[6], batch[7], batch[11]
    assert np.array_equal(render['triangles'], triangles.reshape(-1)[start:start + count])
    assert render['sourceMaterial'] == material
    mat = struct.unpack_from('<16I', root['MOMT'], material * 64)
    assert render['sourceRecord'] == list(mat)
    tex_offset = mat[3]; tex_path = root['MOTX'][tex_offset:root['MOTX'].find(b'\0', tex_offset)].decode('utf8')
    tex_raw, _ = archives.read(tex_path)
    assert manifest['textures'][render['texture']]['source']['sha256'] == hashlib.sha256(tex_raw).hexdigest()
    stats['wmoPasses'] += 1; stats['wmoRenderTriangles'] += count // 3
   walkable = np.asarray(group['walkableTriangles'], dtype=int).reshape(-1, 3)
   tri_points = positions[walkable]; normal = np.cross(tri_points[:, 1] - tri_points[:, 0], tri_points[:, 2] - tri_points[:, 0])
   assert np.all(normal[:, 1] / np.maximum(np.linalg.norm(normal, axis=1), 1e-8) > np.sqrt(.5))
   stats['wmoCollisionTriangles'] += len(selected); stats['wmoWalkableTriangles'] += len(walkable)
   stats['wmoGroups'] += 1; stats['wmoVertices'] += len(positions)
   all_vertices.append(positions); all_collision.append(selected + offset); offset += len(positions)
  vertices = np.concatenate(all_vertices); solid = np.concatenate(all_collision)
  assert np.array_equal(vertices.min(0), building['boundsMin']) and np.array_equal(vertices.max(0), building['boundsMax'])
  for entrance in building['entrances']:
   pi = entrance['sourcePortalIndex']; start, count, *_ = struct.unpack_from('<HH4f', root['MOPT'], pi * 20)
   points = unity(np.frombuffer(root['MOPV'], dtype='<f4').reshape(-1, 3)[start:start + count])
   assert np.array_equal(points.mean(0), entrance['center'])
   assert float(points[:, 1].min()) == entrance['floorY']
   outward = np.array(entrance['outward']); assert abs(np.linalg.norm(outward) - 1) < 1e-5
   center = np.array(entrance['center']); center[1] = entrance['floorY'] + min(1.6, entrance['height'] * .45)
   # A short ray through the real portal opening must not meet a wall. Width
   # offsets sample the middle half of the aperture, not just one lucky point.
   tangent = np.array([outward[2], 0, -outward[0]])
   for side in [-.2, 0, .2]:
    point = center + tangent * entrance['width'] * side
    hits = segment_hits(point + outward * .65, point - outward * .65, vertices, solid)
    assert len(hits) == 0, (id, pi, 'door collision', side, hits.tolist())
    stats['entranceRaysClear'] += 1
   stats['originalEntrances'] += 1
  assert len(building['entrances']) > 0
  stats['buildings'] += 1
 return stats
