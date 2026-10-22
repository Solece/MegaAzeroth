"""Decode pinned original Azeroth menu/loading textures from owned local MPQs."""
from pathlib import Path
import argparse,hashlib,json,sys

def sha(data):return hashlib.sha256(data).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--repo',type=Path,required=True);p.add_argument('--data-dir',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 data=a.data_dir.resolve(strict=True);out=a.output.resolve()
 if data==out or data in out.parents:raise ValueError('Output must be separate from the owned source Data directory')
 sys.path.insert(0,str(a.repo.resolve()/'scripts/azeroth-assets'))
 from enemy_fidelity_mpq import Archives
 from enemy_fidelity_blp import decode_blp
 from PIL import Image
 recipes=json.loads(Path(__file__).with_name('menu-loading-recipes.json').read_text(encoding='utf8'));out.mkdir(parents=True,exist_ok=True);rows=[]
 with Archives(data) as archives:
  for recipe in recipes['assets']:
   original,archive=archives.read(recipe['sourcePath'])
   if sha(original)!=recipe['sourceSha256']:raise ValueError('Unsupported original texture version: '+recipe['sourcePath'])
   im=decode_blp(original).convert('RGBA');pixels=im.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
   if im.size!=(recipe['width'],recipe['height']) or sha(pixels)!=recipe['rgbaSha256']:raise ValueError('Decoded source pixels differ: '+recipe['sourcePath'])
   im.save(out/recipe['png']);(out/recipe['rgba']).write_bytes(pixels);(out/recipe['size']).write_text(f'{im.width} {im.height}\n',encoding='ascii');rows.append(dict(recipe,actualArchive=archive))
 (out/'source-export.json').write_text(json.dumps(dict(format='azeroth-menu-loading-export-v1',complete=True,assets=rows),indent=2)+'\n',encoding='utf8');print('Verified original source textures:',len(rows));return 0
if __name__=='__main__':
 try:sys.exit(main())
 except Exception as e:print('Source export failed: '+str(e),file=sys.stderr);sys.exit(1)
