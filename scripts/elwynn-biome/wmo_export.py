"""Original Vanilla WMO v17 buildings, groups, render batches and collision faces.

Layout reference: wowdev/pywowlib file_formats/wmo_format_{root,group}.py.
The collision selection uses explicit MOPY collision faces and non-detail render
faces, as used by TrinityCore's vmap extractor. No external source code is copied.
"""
from pathlib import Path
import collections, json, math, struct
import numpy as np

BUILDINGS = {
 'farm': r'World\WMO\Azeroth\Buildings\Human_Farm\Farm.wmo',
 'goldshireblacksmith': r'World\WMO\Azeroth\Buildings\GoldshireBlacksmith\GoldshireBlacksmith.wmo',
 'goldshireinn': r'World\WMO\Azeroth\Buildings\GoldshireInn\GoldshireInn.wmo',
}

def chunks(data, start=0):
 offset = start
 while offset + 8 <= len(data):
  name = data[offset:offset + 4][::-1].decode('ascii')
  count = struct.unpack_from('<I', data, offset + 4)[0]
  if offset + 8 + count > len(data):
   raise ValueError(f'Invalid WMO chunk {name} at {offset} size {count}')
  yield name, data[offset + 8:offset + 8 + count]
  offset += 8 + count
 if offset != len(data): raise ValueError('Trailing incomplete WMO chunk')

def text(table, offset):
 if offset >= len(table): return ''
 end = table.find(b'\0', offset)
 if end < 0: raise ValueError('Unterminated WMO string')
 return table[offset:end].decode('utf8')

def decode_wmo(path, read, texture, unity):
 data, source = read(path); root = dict(chunks(data))
 version = struct.unpack('<I', root['MVER'])[0]
 if version != 17: raise ValueError('Only verified original WMOv17 is supported')
 header = struct.unpack_from('<9I6f2H', root['MOHD'])
 materials = [struct.unpack_from('<16I', root['MOMT'], i) for i in range(0, len(root['MOMT']), 64)]
 if len(materials) != header[0]: raise ValueError('WMO root material count mismatch')
 material_data = []
 for index, mat in enumerate(materials):
  path1 = text(root['MOTX'], mat[3])
  if mat[1] != 0 or mat[2] not in [0, 1]:
   raise ValueError(f'Unsupported WMO shader/blend {mat[1:3]}: {path}')
  material_data.append(dict(texture=texture(path1, 3), sourceMaterial=index,
      sourceFlags=mat[0], shader=mat[1], blendMode=mat[2], sourceTexture=path1,
      renderFlags=4 if mat[0] & 4 else 0, sourceRecord=list(mat)))
 groups = []; group_evidence = []; union_positions = []
 for index in range(header[1]):
  group_path = path[:-4] + f'_{index:03}.wmo'
  group_bytes, group_source = read(group_path); outer = dict(chunks(group_bytes))
  if struct.unpack('<I', outer['MVER'])[0] != version: raise ValueError('WMO group version mismatch')
  gp = outer['MOGP']; group_flags = struct.unpack_from('<I', gp, 8)[0]
  arrays = dict(chunks(gp, 68))
  source_positions = np.frombuffer(arrays['MOVT'], dtype='<f4').reshape(-1, 3)
  positions = unity(source_positions)
  normals = unity(np.frombuffer(arrays['MONR'], dtype='<f4').reshape(-1, 3))
  uv = np.frombuffer(arrays['MOTV'], dtype='<f4').reshape(-1, 2).copy(); uv[:, 1] = 1 - uv[:, 1]
  if positions.shape != normals.shape or positions.shape[0] != uv.shape[0]: raise ValueError('WMO vertex array mismatch')
  triangles = np.frombuffer(arrays['MOVI'], dtype='<u2').reshape(-1, 3)[:, [0, 2, 1]].astype(np.int32)
  mopy = np.frombuffer(arrays['MOPY'], dtype='u1').reshape(-1, 2)
  if len(mopy) != len(triangles) or triangles.max() >= len(positions): raise ValueError('WMO triangle array mismatch')
  batches = []; covered = np.zeros(len(triangles), dtype=bool)
  for batch_index, offset in enumerate(range(0, len(arrays['MOBA']), 24)):
   b = struct.unpack_from('<6hI3H2B', arrays['MOBA'], offset)
   start, count, first, last, flags, material_id = b[6:]
   if start % 3 or count % 3 or start + count > len(triangles) * 3: raise ValueError('Invalid source WMO batch range')
   if material_id >= len(materials): raise ValueError('Invalid WMO batch material')
   tri = triangles[start // 3:(start + count) // 3]
   if tri.min() < first or tri.max() > last: raise ValueError('WMO batch vertex bounds mismatch')
   covered[start // 3:(start + count) // 3] = True
   batches.append(dict(**material_data[material_id], sourceBatch=batch_index,
       sourceStartIndex=start, sourceIndexCount=count, sourceBatchFlags=flags,
       triangles=tri.reshape(-1).tolist()))
  render = (mopy[:, 0] & 0x20) != 0
  if not np.array_equal(covered, render): raise ValueError('WMO batch coverage differs from source rendered triangles')
  collision = ((mopy[:, 0] & 0x08) != 0) | (render & ((mopy[:, 0] & 0x04) == 0))
  solid = triangles[collision]
  v = positions[solid]
  # Source normals are transformed separately; derive collision face slopes
  # geometrically from the converted left-handed winding.
  face_normals = np.cross(v[:, 1] - v[:, 0], v[:, 2] - v[:, 0])
  lengths = np.linalg.norm(face_normals, axis=1)
  slope = face_normals[:, 1] / np.maximum(lengths, 1e-8)
  # Reflection to Unity followed by reversed winding preserves the direction
  # of source upward-facing geometric normals.
  walkable = solid[slope > math.cos(math.radians(45))]
  if len(solid) == 0: raise ValueError('Expected original building collision faces')
  colors = []
  if 'MOCV' in arrays:
   rgba = np.frombuffer(arrays['MOCV'], dtype='u1').reshape(-1, 4)[:, [2, 1, 0, 3]]
   if len(rgba) != len(positions): raise ValueError('WMO vertex color count mismatch')
   colors = rgba.reshape(-1).tolist()
  group_name = text(root['MOGN'], struct.unpack_from('<I', gp, 0)[0]) if 'MOGN' in root else ''
  groups.append(dict(id=index, name=group_name, sourceFlags=group_flags,
      vertexCount=len(positions), positions=positions.reshape(-1).tolist(), normals=normals.reshape(-1).tolist(),
      uv=uv.reshape(-1).tolist(), colors=colors, passes=batches,
      collisionTriangles=solid.reshape(-1).tolist(), walkableTriangles=walkable.reshape(-1).tolist(),
      boundsMin=positions.min(0).tolist(), boundsMax=positions.max(0).tolist(),
      source=dict(**group_source, wmoVersion=version, mopyFlags=list(map(int, mopy[:, 0])),
          mopyMaterials=list(map(int, mopy[:, 1])), sourceTriangleCount=len(triangles),
          collisionRule='MOPY explicit collision0x08 OR(render0x20 AND NOT detail0x04)',
          sourceBatchCount=len(batches), sourceWalkableTriangleCount=len(walkable))))
  union_positions.append(positions); group_evidence.append(group_source)
 vertices = np.concatenate(union_positions)
 portal_vertices = unity(np.frombuffer(root['MOPV'], dtype='<f4').reshape(-1, 3))
 portal_refs = [struct.unpack_from('<HHhH', root['MOPR'], i) for i in range(0, len(root['MOPR']), 8)]
 portals = []; entrances = []
 for index, offset in enumerate(range(0, len(root['MOPT']), 20)):
  start, count, nx, ny, nz, distance = struct.unpack_from('<HH4f', root['MOPT'], offset)
  points = portal_vertices[start:start + count]
  refs = [r for r in portal_refs if r[0] == index]
  center = points.mean(0); low = points.min(0); high = points.max(0)
  normal = unity([[nx, ny, nz]])[0]
  portals.append(dict(sourcePortal=index, positions=points.reshape(-1).tolist(), normal=normal.tolist(), distance=distance,
      groupRefs=[dict(group=r[1], side=r[2]) for r in refs], boundsMin=low.tolist(), boundsMax=high.tolist()))
  outdoor = [r[1] for r in refs if groups[r[1]]['sourceFlags'] & 8]
  indoor = [r[1] for r in refs if groups[r[1]]['sourceFlags'] & 0x2000]
  if not outdoor or not indoor or high[1] - low[1] < 1: continue
  indoor_group = groups[indoor[0]]
  indoor_center = (np.array(indoor_group['boundsMin']) + np.array(indoor_group['boundsMax'])) * .5
  normal[1] = 0
  if np.dot(normal, center - indoor_center) < 0: normal *= -1
  normal /= max(float(np.linalg.norm(normal)), 1e-6)
  entrances.append(dict(sourcePortalIndex=index, center=center.tolist(), outward=normal.tolist(),
      width=float(np.linalg.norm((high - low)[[0, 2]])), height=float(high[1] - low[1]), floorY=float(low[1]),
      boundsMin=low.tolist(), boundsMax=high.tolist(), indoorGroup=indoor, outdoorGroup=outdoor))
 if not entrances: raise ValueError('No original outdoor-to-indoor entrance found: ' + path)
 return dict(format='azeroth-elwynn-wmo-v1', wmoVersion=version, source=source,
     boundsMin=vertices.min(0).tolist(), boundsMax=vertices.max(0).tolist(),
     floorY=min(e['floorY'] for e in entrances), groups=groups, portals=portals, entrances=entrances,
     materials=material_data, sourceGroupEvidence=group_evidence,
     excluded=['MODD placed furniture/torches/doodads and native WMO local lights are not emitted',
       'No WMO portal visibility culling is applied: all actual indoor/outdoor groups render'])

def export_buildings(out, read, texture, unity):
 (out / 'buildings').mkdir(exist_ok=True); result = {}
 for id, path in BUILDINGS.items():
  building = decode_wmo(path, read, texture, unity); building['id'] = id
  file = 'buildings/' + id + '.json'
  (out / file).write_text(json.dumps(building, separators=(',', ':')), encoding='utf8')
  result[id] = dict(file=file, groupCount=len(building['groups']),
      vertexCount=sum(g['vertexCount'] for g in building['groups']),
      passCount=sum(len(g['passes']) for g in building['groups']),
      renderTriangles=sum(len(p['triangles']) // 3 for g in building['groups'] for p in g['passes']),
      collisionTriangles=sum(len(g['collisionTriangles']) // 3 for g in building['groups']),
      walkableTriangles=sum(len(g['walkableTriangles']) // 3 for g in building['groups']),
      boundsMin=building['boundsMin'], boundsMax=building['boundsMax'], floorY=building['floorY'],
      entranceCount=len(building['entrances']))
 return result
