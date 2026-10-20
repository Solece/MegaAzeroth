from pathlib import Path
from PIL import Image
import json,hashlib
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
assets=root/'private-assets/azeroth-ui/plugin/assets'
rows=[]
for p in sorted(assets.rglob('*.png')):
 with Image.open(p) as src:
  image=src.convert('RGBA'); width,height=image.size
  pixels=image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
 if len(pixels)!=width*height*4: raise ValueError(str(p))
 raw=p.with_suffix('.rgba');raw.write_bytes(pixels)
 p.with_suffix('.size').write_text(f'{width} {height}\n',encoding='ascii')
 rows.append({'png':str(p.relative_to(assets)),'width':width,'height':height,'format':'RGBA32','rowOrder':'bottom-up','rawBytes':len(pixels),'rawSha256':hashlib.sha256(pixels).hexdigest()})
(root/'reports/azeroth-ui/raw-assets-manifest.json').write_text(json.dumps(rows,indent=2))
print(f'Converted {len(rows)} PNGs, {sum(x["rawBytes"] for x in rows)} raw bytes. Originals retained.')
