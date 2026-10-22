"""Package already converted runtime assets; dereference owned asset junctions.

Read-only source runtime. Output contains no original MPQ/M2/BLP/DBC/WMO or
executables. Game, loader and mod DLL packaging is a separate operation.
"""
from pathlib import Path,PurePosixPath
import argparse,collections,hashlib,json,re,sys,zipfile
ROOTS=['AzerothAssets','AzerothMageAssets','AzerothHeroAssets','AzerothElwynnAssets','BepInEx/plugins/AzerothAbilities','BepInEx/plugins/AzerothHud','BepInEx/plugins/HeroRoster','BepInEx/plugins/MageTalents']
ALLOWED={'.json','.bin','.rgba','.size','.png','.ttf'}
OMIT_NAMES={'conversion.json','source-evidence.json','validation.json','motion-validation.json','export-summary.json','rebuild-manifest.json','asset-augmentation.json','source-vfx.json','binding-audit.json','all-class-binding-audit.json'}
ABSOLUTE=re.compile(r'(?<![A-Za-z])[A-Za-z]:[\\/]')

def digest(data):return hashlib.sha256(data).hexdigest()
def walk(root,ancestors=frozenset()):
 resolved=root.resolve(strict=True)
 if resolved in ancestors:raise ValueError('Directory link cycle: '+str(root))
 for p in sorted(root.iterdir(),key=lambda p:p.name.casefold()):
  if p.is_dir():yield from walk(p,ancestors|{resolved})
  elif p.is_file():yield p
  else:raise ValueError('Unsupported filesystem entry: '+str(p))
def path_allowed(relative):
 p=PurePosixPath(relative)
 if p.suffix.lower() not in ALLOWED:return False,'not converted runtime data'
 if p.name in OMIT_NAMES or p.name.startswith('preview.'):return False,'conversion report or preview'
 if len(p.parts)>1 and p.parts[0]=='AzerothAssets' and p.parts[1]=='raw':return False,'original extraction cache'
 if len(p.parts)>2 and p.parts[:2]==('AzerothHeroAssets','vfx') and p.parts[2]!='assets':return False,'VFX conversion report'
 if '.dll' in p.suffixes:return False,'DLL is packaged separately'
 return True,None

def sanitize(value,location='',changes=None):
 if isinstance(value,dict):return {k:sanitize(v,location+'/'+k,changes) for k,v in value.items()}
 if isinstance(value,list):return [sanitize(v,location+'/'+str(i),changes) for i,v in enumerate(value)]
 if isinstance(value,str) and ABSOLUTE.search(value):
  # Only source provenance carries workstation paths. Never rewrite a render
  # asset reference or animation filename as a packaging workaround.
  if not (location=='/source' or location.startswith('/sourceCatalogs/') and location.endswith('/path')):raise ValueError('Unexpected absolute path in runtime field '+location+': '+value)
  leaf=value.replace('\\','/').rsplit('/',1)[-1]
  replacement='source-owned/'+leaf;changes.append({'Field':location,'ReplacedWith':replacement});return replacement
 return value

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--runtime',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--archive-name',default='MegaAzeroth-prepared-assets.zip');args=parser.parse_args()
 runtime=args.runtime.resolve(strict=True);output=args.output.resolve()
 if output==runtime or runtime in output.parents:raise ValueError('Output must be separate from source runtime')
 payload=output/'Runtime'
 if payload.exists() and any(payload.iterdir()):raise ValueError('Output payload must be fresh')
 payload.mkdir(parents=True,exist_ok=True);files=[];excluded=[];sanitized=[];links=[]
 optional=['BepInEx/plugins/AzerothPresentation']
 for relative_root in ROOTS+[r for r in optional if (runtime/r).is_dir()]:
  root=runtime/relative_root
  if not root.is_dir():raise FileNotFoundError(root)
  links.append({'Path':relative_root,'ResolvedSource':str(root.resolve())})
  for p in walk(root):
   relative=p.relative_to(runtime).as_posix();allow,reason=path_allowed(relative)
   if not allow:excluded.append({'Path':relative,'Reason':reason});continue
   data=p.read_bytes();source_hash=digest(data);changes=[]
   if p.suffix.lower()=='.json':
    document=json.loads(data.decode('utf-8-sig'));clean=sanitize(document,changes=changes)
    if changes:data=(json.dumps(clean,indent=2,ensure_ascii=False)+'\n').encode('utf8');sanitized.append({'Path':relative,'Changes':changes,'SourceSha256':source_hash,'PreparedSha256':digest(data)})
    if ABSOLUTE.search(data.decode('utf8')):raise ValueError('Workstation path survives JSON audit: '+relative)
   target=payload/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data);files.append({'Path':relative,'Bytes':len(data),'Sha256':digest(data)})
 files.sort(key=lambda row:row['Path'].casefold());names=[x['Path'].casefold() for x in files]
 if len(names)!=len(set(names)):raise ValueError('Duplicate case-insensitive asset path')
 manifest={'Format':'megaazeroth-prepared-assets-v1','Files':files};manifest_path=output/'asset-files.json';manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf8')
 # Byte validation reads copied payload independently from the source loop.
 for row in files:
  data=(payload/row['Path']).read_bytes()
  if len(data)!=row['Bytes'] or digest(data)!=row['Sha256']:raise ValueError('Copied payload mismatch: '+row['Path'])
 archive=output/args.archive_name
 with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True) as z:
  for row in files:
   info=zipfile.ZipInfo(row['Path'],date_time=(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;z.writestr(info,(payload/row['Path']).read_bytes(),compresslevel=6)
 with zipfile.ZipFile(archive) as z:
  if set(z.namelist())!={row['Path'] for row in files}:raise ValueError('ZIP inventory differs from manifest')
  for row in files:
   data=z.read(row['Path'])
   if len(data)!=row['Bytes'] or digest(data)!=row['Sha256']:raise ValueError('ZIP member mismatch: '+row['Path'])
 summary={'Format':'megaazeroth-prepared-assets-audit-v1','Complete':True,'SourceRuntime':str(runtime),'ResolvedRoots':links,'Payload':str(payload),'Manifest':{'Path':manifest_path.name,'Sha256':digest(manifest_path.read_bytes())},'Archive':{'Path':archive.name,'Bytes':archive.stat().st_size,'Sha256':digest(archive.read_bytes())},'FileCount':len(files),'PreparedBytes':sum(row['Bytes'] for row in files),'Excluded':excluded,'SanitizedSourceProvenance':sanitized,'ByRoot':dict(collections.Counter(row['Path'].split('/')[0] if not row['Path'].startswith('BepInEx/') else '/'.join(row['Path'].split('/')[:3]) for row in files)),'Validation':{'DereferencedCopies':True,'ManifestHashesPassed':True,'ArchiveAllMembersPassed':True,'NoOriginalGameFormats':True,'NoAbsoluteWorkstationPaths':True,'NativeGameplayLaunched':False}}
 (output/'pack-audit.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf8');print(json.dumps({k:v for k,v in summary.items() if k not in ['Excluded','ResolvedRoots','SanitizedSourceProvenance']},indent=2))
 return 0
if __name__=='__main__':
 try:sys.exit(main())
 except Exception as e:print('Packaging failed: '+str(e),file=sys.stderr);sys.exit(1)
