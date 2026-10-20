from pathlib import Path
import sys,json,io
from PIL import Image
root=Path(r'D:/Games/Saitan/AzerothMegabonkLab');sys.path.insert(0,str(root/'scripts/azeroth-assets'));from mpq_source import Archives
catalog=json.loads((root/'src/MageTalents/mage-talents.json').read_text(encoding='utf8'));ids={'ignite','presence_of_mind','splitting_ice','pyroclasm','water_elemental'};ar=Archives();out=root/'private-assets/azeroth-ui/postcombat-assets/Interface/Icons';out.mkdir(parents=True,exist_ok=True)
for talent in catalog['talents']:
 if talent['id'] not in ids:continue
 stem=talent['icon'];path='Interface\\Icons\\'+stem+'.blp';data,archive=ar.read(path);image=Image.open(io.BytesIO(data)).convert('RGBA');image.save(out/(stem+'.png'));(out/(stem+'.rgba')).write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes());(out/(stem+'.size')).write_text(f'{image.width} {image.height}\n',encoding='ascii');print(talent['id'],stem,archive)
