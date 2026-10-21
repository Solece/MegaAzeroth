"""Rebuild private death/ghost runtime addons from user-owned original MPQs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys

from enemy_fidelity_mpq import Archives
from enemy_fidelity_m2 import convert
from enemy_fidelity_profiles import export_death_profiles, export_ghost_profiles

DEATH_KEYS = {'boar', 'bonegolem', 'gnoll', 'infernal', 'kobold', 'lich', 'murloc', 'ogre', 'skeleton', 'tarantula', 'wolf'}
GHOST_FILES = ['attack.vertices.bin', 'cast.vertices.bin', 'cast_omni.vertices.bin', 'death-profile.json',
               'death.vertices.bin', 'ghost-material-profile.json', 'idle.vertices.bin', 'model.json',
               'run.vertices.bin', 'texture.rgba', 'texture.rgba.json', 'walk.vertices.bin']
GHOST_SOURCE_SHA256 = '6f0bad96f2205b44dac625a6222b6c2306f9ccd336b3cf732a89891289a0ca5b'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=['all', 'death', 'ghost'], default='all')
    parser.add_argument('--data-dir', default=os.environ.get('AZEROTH_DATA_DIR'),
                        help='Owned Azeroth Data folder containing patch.MPQ through patch-7.MPQ; or AZEROTH_DATA_DIR')
    parser.add_argument('--base-assets', default=os.environ.get('AZEROTH_BASE_ASSETS'),
                        help='Existing privately generated eleven-model folder (model.json + original death frame files); or AZEROTH_BASE_ASSETS')
    parser.add_argument('--output-root', default=os.environ.get('AZEROTH_FIDELITY_OUTPUT'),
                        help='Explicit ignored private output folder; or AZEROTH_FIDELITY_OUTPUT')
    args = parser.parse_args()
    if not args.data_dir or not args.output_root:
        parser.error('--data-dir and --output-root (or their environment variables) are required')
    if args.mode != 'ghost' and not args.base_assets:
        parser.error('--base-assets (or AZEROTH_BASE_ASSETS) is required for death profiles')
    data = Path(args.data_dir).resolve(strict=True)
    output = Path(args.output_root).resolve()
    script_root = Path(__file__).resolve().parent
    if output == script_root or script_root in output.parents:
        parser.error('output must be an ignored private folder outside this script/package directory')
    if output == data or data in output.parents:
        parser.error('output must be outside the original source Data directory')
    runtime = output / 'assets' / 'AzerothAssets'
    runtime.mkdir(parents=True, exist_ok=True)
    summaries = {}
    with Archives(data) as archives:
        if args.mode in ('all', 'death'):
            base = Path(args.base_assets).resolve(strict=True)
            missing = sorted(key for key in DEATH_KEYS if not (base / key / 'model.json').is_file())
            if missing:
                raise FileNotFoundError('Missing privately generated baseline models: ' + ', '.join(missing))
            profiles = export_death_profiles(archives, base, runtime)
            if {row['key'] for row in profiles} != DEATH_KEYS:
                raise ValueError('Death exporter produced a different source model set')
            summaries['death'] = {'models': len(profiles), 'sequences': sum(len(row['profile']['animations']) for row in profiles)}
        if args.mode in ('all', 'ghost'):
            original, archive = archives.read('Creature\\Ghost\\Ghost.m2')
            if hashlib.sha256(original).hexdigest() != GHOST_SOURCE_SHA256:
                raise ValueError('Original Ghost source version differs from the verified source; refusing an unverified profile')
            scratch = output / 'scratch' / 'ghost'
            convert(archives, 'Ghost', ['GhostSkin'], 146, model_path='Creature\\Ghost\\Ghost.m2',
                    skin_folder='Creature\\Ghost', output_dir=scratch)
            summaries['ghost'] = export_ghost_profiles(scratch)
            summaries['ghost']['sourceArchive'] = archive
            target = runtime / 'ghost'
            target.mkdir(parents=True, exist_ok=True)
            for name in GHOST_FILES:
                shutil.copy2(scratch / name, target / name)
    reference = json.loads((script_root / 'enemy-fidelity-reference.json').read_text(encoding='utf-8'))
    expected = [row for row in reference['files'] if args.mode == 'all' or
                (args.mode == 'ghost') == row['path'].startswith('AzerothAssets/ghost/')]
    rows = []
    for row in expected:
        path = output / 'assets' / row['path']
        if not path.is_file() or path.stat().st_size != row['bytes'] or sha256(path) != row['sha256']:
            raise ValueError('Generated runtime file differs from frozen source-verified reference: ' + row['path'])
        rows.append({'path': row['path'], 'bytes': row['bytes'], 'sha256': row['sha256']})
    report = {'format': 'azeroth-enemy-fidelity-rebuild-v1', 'passed': True, 'mode': args.mode,
              'runtimeFiles': rows, 'summaries': summaries, 'nativeGameplayVerified': False,
              'sourcePolicy': 'Read-only user-owned original MPQ; generated binaries remain private and must not be committed'}
    (output / 'rebuild-manifest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'passed': True, 'runtimeFiles': len(rows), 'summaries': summaries}))
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        raise SystemExit(1)
