"""Shared local paths for the private Azeroth VFX export; no asset data here."""
import argparse
import os
import sys
from pathlib import Path


def arguments(description, catalogs=False):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument('--repo', type=Path, default=Path(os.environ.get('AZEROTH_LAB_ROOT', Path(__file__).resolve().parents[2])),
                        help='Repository root; defaults to this scripts/azeroth-vfx checkout')
    parser.add_argument('--work', '--out', dest='work', type=Path,
                        default=Path(os.environ['AZEROTH_VFX_WORK']) if os.environ.get('AZEROTH_VFX_WORK') else None,
                        help='Private output directory; default <repo>/private-assets/azeroth-vfx')
    parser.add_argument('--source-data', type=Path,
                        default=Path(os.environ['AZEROTH_SOURCE_DATA']) if os.environ.get('AZEROTH_SOURCE_DATA') else None,
                        help='User-owned AzerothSurvivors/Data MPQ directory')
    if catalogs:
        parser.add_argument('--catalogs', type=Path,
                            help='Runtime catalogs directory or source export containing abilities/; default <repo>/src/AzerothAbilities/catalogs')
    args = parser.parse_args()
    args.repo = args.repo.expanduser().resolve()
    args.work = (args.work or args.repo/'private-assets/azeroth-vfx').expanduser().resolve()
    args.source_data = (args.source_data or args.repo.parent/'AzerothSurvivorsWindowsNew/AzerothSurvivors/Data').expanduser().resolve()
    # Generated M2/BLP-derived data must not be emitted into tracked source dirs.
    try:
        relative = args.work.relative_to(args.repo)
    except ValueError:
        relative = None
    if relative is not None and (not relative.parts or relative.parts[0] not in {'private-assets', 'reports', 'cache'}):
        parser.error('--work inside the repository must use its ignored private-assets/, reports/ or cache/ directory')
    for directory in (args.repo/'scripts/azeroth-assets', args.repo/'scripts/azeroth-mage'):
        if not directory.is_dir():
            parser.error('Missing existing repository helper directory: '+str(directory))
        sys.path.insert(0, str(directory))
    if not args.source_data.is_dir():
        parser.error('User-owned MPQ directory is absent: '+str(args.source_data))
    # The existing MPQ reader accepts a module-global data root. Change only
    # this process configuration; neither helper code nor original files change.
    import mpq_source
    mpq_source.DATA = args.source_data
    if catalogs:
        args.catalogs = (args.catalogs or args.repo/'src/AzerothAbilities/catalogs').expanduser().resolve()
    args.work.mkdir(parents=True, exist_ok=True)
    return args
