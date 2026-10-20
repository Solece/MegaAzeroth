"""Export verified polymorph display 856 using the existing local M2 reader.

Keep the creature converter unchanged; retarget its output into the Mage pack.
"""
from pathlib import Path
import inspect,sys,json
from PIL import Image
ROOT=Path(r'D:/Games/Saitan/AzerothMegabonkLab')
sys.path.insert(0,str(ROOT/'scripts/azeroth-assets'))
from blp_decode import decode_blp
import convert_m2
code=inspect.getsource(convert_m2.convert)
code=code.replace("ROOT/'private-assets/azeroth'", "ROOT/'private-assets/azeroth-mage'")
code=code.replace("ROOT/f'reports/azeroth-assets/{name.lower()}-conversion.json'", "out/'conversion.json'")
code=code.replace("Image.open(io.BytesIO(tex)).convert('RGBA')", "decode_blp(tex)")
code=code.replace("Image.open(io.BytesIO(tb)).convert('RGBA')", "decode_blp(tb)")
scope=dict(convert_m2.__dict__,decode_blp=decode_blp)
exec(compile(code,__file__,'exec'),scope)
out=scope['convert']('Sheep','Sheep',856)
im=Image.open(out/'texture.png').convert('RGBA')
(out/'texture.rgba').write_bytes(im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
(out/'texture.rgba.json').write_text(json.dumps(dict(width=im.width,height=im.height,format='RGBA32',rowOrder='bottom-up')))
