"""Prepare a private, self-contained snapshot of a validated local installation.

This is an archival tool, not a licence to redistribute the included games.
It never modifies either source installation or includes profiles and credentials.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import zipfile


GAME_PINS = {
    "Megabonk.exe": "e2fa5120fbf41837891e0ca372210865b0edd3826abdb0e36d263b0f40fd5f01",
    "GameAssembly.dll": "4350a7ae25ba7aec35677c213fdceabbb638676bb00ab59131d7d9c37b6e3d9e",
    "Megabonk_Data/il2cpp_data/Metadata/global-metadata.dat": "17b506a77616b943b414b9a42d35e3acbdaf71eec5dc6c470fc4da338010d550",
}
BEPINEX_SHA = "a3ec977c7760c6045b742a9bf3e59c7b11baa80b023f402ebdbff07bad6b0d96"


def sha(path: Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def copy_tree(source: Path, destination: Path) -> None:
    if source.is_dir():
        for item in sorted(source.iterdir()):
            copy_tree(item, destination / item.name)
    else:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--game", type=Path, required=True)
    p.add_argument("--validated-runtime", type=Path, required=True)
    p.add_argument("--prepared-assets", type=Path, required=True)
    p.add_argument("--inventory", type=Path, required=True)
    p.add_argument("--bepinex-archive", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--source-commit", default="815a5f2be6c0d0aeb2a8cdebc8809386c3be8ec4")
    args = p.parse_args()
    if args.output.exists():
        raise SystemExit("Output already exists; choose a fresh staging directory.")
    for name, expected in GAME_PINS.items():
        if sha(args.game / name) != expected:
            raise SystemExit(f"Unsupported game: {name}")
        if sha(args.validated_runtime / name) != expected:
            raise SystemExit(f"Validated runtime differs from pinned game: {name}")
    if sha(args.bepinex_archive) != BEPINEX_SHA:
        raise SystemExit("Loader archive hash mismatch.")
    inventory = json.loads(args.inventory.read_text(encoding="utf-8-sig"))
    if len(inventory["OurDlls"]) < 10:
        raise SystemExit("Expected the ten core validated mod assemblies.")
    for row in inventory["OurDlls"]:
        relative = PurePosixPath(row["Path"])
        if relative.is_absolute() or ".." in relative.parts or not str(relative).startswith("BepInEx/plugins/") or relative.suffix != ".dll":
            raise SystemExit("Unexpected mod inventory path: " + row["Path"])
    runtime = args.output / "Runtime"
    runtime.mkdir(parents=True)
    for name in ("Megabonk.exe", "GameAssembly.dll", "UnityPlayer.dll", "baselib.dll",
                 "UnityCrashHandler64.exe", "D3D12", "Megabonk_Data"):
        copy_tree(args.game / name, runtime / name)
    print("Original game snapshot copied.", flush=True)
    with zipfile.ZipFile(args.bepinex_archive) as archive:
        for member in archive.infolist():
            if not member.filename.startswith("BepInExPack/"):
                continue
            relative = PurePosixPath(member.filename.removeprefix("BepInExPack/"))
            if not relative.parts or member.is_dir():
                continue
            if relative.is_absolute() or ".." in relative.parts or ":" in str(relative):
                raise SystemExit("Unsafe loader archive member.")
            target = runtime.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, target.open("wb") as dest:
                shutil.copyfileobj(source, dest)
    copy_tree(args.validated_runtime / "BepInEx/interop", runtime / "BepInEx/interop")
    for row in inventory["OurDlls"]:
        source = args.validated_runtime / row["Path"]
        if sha(source) != row["Sha256"].lower():
            raise SystemExit("Mod DLL differs from accepted v5: " + row["Path"])
        copy_tree(source, runtime / row["Path"])
    copy_tree(args.prepared_assets, runtime)
    config = runtime / "BepInEx/config"
    config.mkdir(exist_ok=True)
    (config / "BepInEx.cfg").write_text(
        "[IL2CPP]\nUpdateInteropAssemblies = false\n"
        "UnityBaseLibrariesSource = 2023.2.22.zip\n"
        "ScanMethodRefs = true\nDumpDummyAssemblies = false\n"
        "IL2CPPInteropAssembliesPath = {BepInEx}\n"
        "PreloadIL2CPPInteropAssemblies = true\n"
        "GlobalMetadataPath = {GameDataPath}/il2cpp_data/Metadata/global-metadata.dat\n\n"
        "[Logging.Console]\nEnabled = false\n\n"
        "[Logging.Disk]\nEnabled = true\nAppendLog = false\n"
        "InstantFlushing = true\nWriteUnityLog = true\n",
        encoding="utf-8",
    )
    (config / "local.azerothmegabonk.abilities.cfg").write_text(
        "[Abilities]\nSelectedClass = none\nGenericMageRuntime = true\nSpellVisuals = true\n",
        encoding="utf-8",
    )
    (config / "local.azerothmegabonk.hud.cfg").write_text(
        "[HUD]\nEnabled = true\nScale = 1\n", encoding="utf-8"
    )
    # No cached type-loader metadata, native saves, personal configuration or
    # unity-libs downloads are needed: the exact interop build is already pinned.
    files = list(runtime.rglob("*"))
    reparse = [str(x) for x in files if x.is_symlink() or x.is_junction()]
    if reparse:
        raise SystemExit("Package must not contain external links: " + repr(reparse))
    report = {
        "GameVersion": "1.0.69",
        "SteamBuildId": "21750826",
        "SourceModCommit": args.source_commit,
        "Purpose": "Private personal snapshot; no Steam authentication bypass",
        "GameFiles": sum(x.is_file() for x in args.game.rglob("*")),
        "ModAssemblies": len(inventory["OurDlls"]),
        "RuntimeFiles": sum(x.is_file() for x in files),
        "RuntimeBytes": sum(x.stat().st_size for x in files if x.is_file()),
    }
    (args.output / "build-info.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
