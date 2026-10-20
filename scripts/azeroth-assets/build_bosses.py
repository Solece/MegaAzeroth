"""Convert locally installed Azeroth boss-family models; no downloads or game writes."""
import json
from PIL import Image
from convert_m2 import convert,ROOT
BOSSES=[("Ogre","OgreSkinYellow",415),("Infernal","InfernalSkin",169),("Lich",["LichSkin","ArmorReflect3"],7919),("BoneGolem",["BoneGolemSkin","BoneGolemSkullbag"],12073)]
if __name__=="__main__":
 for name,skins,display in BOSSES:
  folder=convert(name,skins,display)
  image=Image.open(folder/"texture.png").convert("RGBA")
  (folder/"texture.rgba").write_bytes(image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes())
  (folder/"texture.rgba.json").write_text(json.dumps(dict(width=image.width,height=image.height,format="RGBA32",rowOrder="bottom-up")))
