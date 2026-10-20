from pathlib import Path
from PIL import Image
import json
root=Path('private-assets/azeroth')
for folder in root.iterdir():
    if not folder.is_dir() or not (folder/'texture.png').exists(): continue
    image=Image.open(folder/'texture.png').convert('RGBA')
    (folder/'texture.rgba').write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
    (folder/'texture.rgba.json').write_text(json.dumps({'width':image.width,'height':image.height,'format':'RGBA32','rowOrder':'bottom-up'}),encoding='utf-8')
    print(folder.name,image.size)
