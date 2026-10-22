# Personal private installation backup

Archive scope: **MegaAzeroth v0.5.0 private**. The user explicitly requested a complete personal game installation archive and a private GitHub repository. This changes the earlier public-mod-only packaging scope: original Megabonk runtime files, its engine files and prepared converted Azeroth resources may be present in this personal archive.

The archive is intended to restore the user's own prepared installation. Its inclusion of game files does not establish permission to publish or redistribute those games or their derived artwork publicly. Original game and third-party terms and notices are retained. This document grants no new rights and assigns no new license.

## Included installation content

- The prepared Megabonk runtime, required engine/data files and the canonical launcher chosen by the packager.
- The ten authored MegaAzeroth plugin modules, required runtime configuration defaults and their prepared catalogs/artwork/mesh/animation/VFX packs.
- The unmodified pinned loader/runtime dependencies and the accompanying `Licenses/`, `CREDITS.md` and `THIRD_PARTY_NOTICES.md`.
- A relative-path file inventory with file sizes and SHA256 values, sufficient to identify the archived build.

Original Azeroth executable files or MPQ archives are not required by the prepared mod at runtime. Include them only if the separately reviewed archive scope explicitly calls for them; the generated packs are already the runtime input.

## Excluded personal and transient data

- Player saves, talent profiles, user identifiers, account data and user-specific preference/profile files. Required mod defaults are freshly prepared or explicitly reviewed, rather than copied wholesale from a personal profile directory.
- Access tokens, API keys, credentials, `.env` files, credential stores, Git metadata and personal tool configuration.
- Game/mod logs, crash dumps, reports, recordings, screenshots, temporary files, shader/download/build caches, `BepInEx/cache`, local development environments and working directories.
- Absolute-path junctions/symlinks to external personal directories. Required asset packs are materialized as ordinary files inside the archive.

Generated interop assemblies may be deliberately retained as runtime dependencies in this private installation. They are distinct from the transient `BepInEx/cache` directory; their presence must be recorded in the archive inventory. This is not the earlier public-mod package exclusion policy.

## Scope of this handoff

This handoff supplies notices and license provenance only. It does not launch a game, publish an archive, alter source installations or verify the final archive's file inventory. The packager records the actual exclusions and final archive hashes separately.

Earlier `check_release.py`, `RELEASE_CHECK.md` and `ForbiddenRoots`/public-exclusion policy fields from the public-release audit are superseded for this private full-installation backup. Do not use those public-only checks as acceptance criteria for this archive.
