"""Index and archive an already prepared private MegaAzeroth snapshot."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import zipfile


def sha(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--package", type=Path, required=True)
    ap.add_argument("--archive", type=Path, required=True)
    ap.add_argument("--release-id", default="v0.5.0-private")
    args = ap.parse_args()
    package = args.package.resolve()
    if args.archive.exists():
        raise SystemExit("Archive exists; choose a fresh output name.")
    files = []
    for path in sorted((package / "Runtime").rglob("*")):
        if path.is_symlink() or path.is_junction():
            raise SystemExit("Reparse point in prepared bundle: " + str(path))
        if not path.is_file():
            continue
        rel = path.relative_to(package / "Runtime").as_posix()
        if rel.endswith((".log", ".dmp")) or rel.startswith("BepInEx/cache/"):
            raise SystemExit("Transient diagnostic file in runtime: " + rel)
        files.append({"Path": rel, "Bytes": path.stat().st_size, "Sha256": sha(path)})
    manifest = {
        "Format": "megaazeroth-bundled-files-v1",
        "ReleaseId": args.release_id,
        "Files": files,
    }
    mp = package / "runtime-files.json"
    mp.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    release = {
        "Format": "megaazeroth-bundled-release-v1",
        "ReleaseId": args.release_id,
        "BundledRuntime": True,
        "RuntimePath": "Runtime",
        "RuntimeFiles": {"Path": "runtime-files.json", "Sha256": sha(mp)},
        "GameVersion": "1.0.69",
        "PersonalArchive": True,
    }
    (package / "release.json").write_text(json.dumps(release, indent=2) + "\n", encoding="utf-8")
    members = sorted(x for x in package.rglob("*") if x.is_file())
    if [x.name for x in package.glob("*.cmd")] != ["Play-MegaAzeroth.cmd"]:
        raise SystemExit("Expected exactly one root launcher.")
    if (package / ".bundle-verified.json").exists():
        raise SystemExit("Do not package a local verification stamp.")
    args.archive.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.archive, "x", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6, allowZip64=True) as z:
        for index, path in enumerate(members):
            z.write(path, "MegaAzeroth/" + path.relative_to(package).as_posix())
            if index % 1000 == 0:
                print(f"Archived {index}/{len(members)} files", flush=True)
    expected = {"MegaAzeroth/" + x.relative_to(package).as_posix(): sha(x) for x in members}
    with zipfile.ZipFile(args.archive) as z:
        if len(z.infolist()) != len(expected):
            raise SystemExit("Archive member count mismatch.")
        for info in z.infolist():
            with z.open(info) as stream:
                actual = hashlib.file_digest(stream, "sha256").hexdigest()
            if actual != expected.pop(info.filename):
                raise SystemExit("Archive member checksum mismatch: " + info.filename)
    if expected:
        raise SystemExit("Missing archive members.")
    digest = sha(args.archive)
    args.archive.with_suffix(args.archive.suffix + ".sha256").write_text(
        digest + "  " + args.archive.name + "\n", encoding="ascii"
    )
    report = {
        "Archive": args.archive.name, "Bytes": args.archive.stat().st_size,
        "Sha256": digest, "Members": len(members), "RuntimeFiles": len(files),
        "RuntimeBytes": sum(x["Bytes"] for x in files),
        "VerifiedAllArchiveMembers": True, "ExternalLinks": 0,
        "PersonalProfilesIncluded": False, "PriorLogsIncluded": False,
        "TimestampUtc": datetime.now(timezone.utc).isoformat(),
    }
    args.archive.with_suffix(".verification.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(report), flush=True)


if __name__ == "__main__":
    main()
