# Original Azeroth reference capture

Run the user-owned **original AzerothSurvivors.exe**, using its existing native quick-start, auto-pick and screenshot facilities. Record the owned game window using the already-installed Universal Modder `um win record` and FFmpeg `gfxcapture`. This does not record a Unity adapter or a software model preview.

## One command

Run with the existing Python environment that contains Universal Modder. Supply paths to the owned original installation, an existing FFmpeg with `gfxcapture`, and a private output/cache directory. This example assumes the scripts have been copied to `scripts/azeroth-reference` in the mod repository:

```powershell
& .\.venv\Scripts\python.exe .\scripts\azeroth-reference\record_original.py --install 'D:\Games\Saitan\AzerothSurvivorsWindowsNew\AzerothSurvivors' --work-root 'E:\AzerothReference' --ffmpeg 'E:\YTSage\ffmpeg.exe' --seconds 120 --launch --record
```

Omit `--launch` for a dry plan. `AZEROTH_REFERENCE_WORK` can replace `--work-root`; `UM_FFMPEG_WIN` can replace `--ffmpeg`. The script does not install dependencies. `--seconds` bounds the owned original process to 5–120 seconds; the verified capture used 120 seconds. Loading a cold shader cache can exceed 35 seconds.

The work directory contains reusable `owned-runtime` and `isolated-profile` directories, plus separate `run-paladin-*` output directories. The script physically copies the source EXE and `Data/*.MPQ`, verifies their hashes and rejects links to the original. It reads the source installation, without launching from or writing into it. Allow approximately 2 GB for the copy and additional space for shader cache, frames and video. The work directory must not overlap the source installation.

## Verified original scenario

The pinned native executable is Azeroth Survivors 0.102.3, observed build `f78e6fab`, SHA256:

```text
2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a
```

The actual original command is:

```text
AzerothSurvivors.exe elwynn paladin human
```

These are native positional arguments, not invented scene flags. They quick-start `Tester`, Human Paladin, in Elwynn Forest. The original starting spell is Consecration, DBC `26573`. This script deliberately fixes the tested scenario rather than claiming untested arbitrary spell selection.

The native original exposes these verified environment controls:

| Control | Meaning used here |
| --- | --- |
| `WOW_ROGUE_SAVE` | Full file path to private `characters.txt`; **not a boolean or a directory**. |
| `BENILLA_HOME` | Private client persistence root for settings and related local state. |
| `WOW_UNATTENDED=1` | Original unattended setup. This is not a claim of a headless renderer. |
| `WOW_ROGUE_AUTOSTART=1`, `WOW_ROGUE_AUTOPICK=1` | Original quick-start and automatic selection. |
| `WOW_ROGUE_AUTOPLAY=3` | Original float-seconds play-start timer. This does not claim autonomous movement AI. |
| `WOW_ROGUE_AUTOLOGOUT` | Original float-seconds logout timer; the runner separately bounds process lifetime. |
| `WOW_ROGUE_SHOTS` | Output **directory** for original renderer PNGs, approximately every 2 seconds, native cap 200. |
| `WOW_ROGUE_UPDATE=0` | Disables the original updater. |
| `WOW_DATA` | Private copied `Data` MPQ directory. |

Inherited `WOW_*` and `BENILLA_HOME` values are removed. `WOW_CAPTURE` is intentionally absent: its presence bypasses `BENILLA_HOME` in this executable's native persistence selector. `WOW_ROGUE_SHOWCASE`, synthetic keyboard controls and arbitrary capture/debug flags are not used because their scenario contracts were not verified. Standard user data and temporary-directory variables also point into the private profile. Normal real profiles and credentials are not imported.

## Background capture and ownership

No clicking, keyboard injection, resizing, `ShowWindow`, `SetForegroundWindow`, desktop recording or focus restoration is performed. The native application can create a visible 640×360 window despite a hidden launch request. It rendered while another application retained foreground during the verified run. A minimized window is not supported.

The script refuses to launch if an original Azeroth process already exists or if the interactive foreground cannot be observed. It enumerates only its own PID's HWNDs, selects the actual visible `Azeroth Survivors` / `Window Class` render window with a nonzero client area, and records that exact HWND/PID. Helper GPU/IME windows are excluded. If its owned original becomes foreground, it stops that process and reports failure. No unrelated process is controlled.

UM records at most two bounded, silent 30-second clips. A second attempt after 70 seconds covers warm-up failure. The ready UM tool owns recording and FFmpeg cleanup. The runner stops only its own original process at the explicit limit, then waits for UM's bounded recording to finish.

## Evidence and pass criteria

Each private run saves exact command, overridden environment, source file hashes, runner snapshot/hash, PID/HWND inventory, foreground events, CPU/private-memory loading telemetry, native game log, native PNGs, UM logs/video and a final `summary.json`. Existing FFmpeg decodes completed videos; optional sibling `ffprobe.exe` adds frame-count/codec metadata. A nonempty MKV or UM exit code 0 alone is insufficient. Capture success requires original PNG headers/dimensions or successfully decoded video frames. It does **not** assert visual equivalence.

The predecessor of this parameterized runner was actually exercised on 2026-10-21: PID 62940, render HWND 1121662, 120.359 seconds, 57 native PNGs and two 640×360/30-fps UM clips. Foreground remained HWND 659244/PID 60436. The source EXE/MPQ size/mtime inventory was unchanged. Native log confirmed Human Paladin, original battle start, real level-up selections and logout. `shot-006.png` visibly contains broad golden Consecration on the ground. The first two capture attempts failed and are retained as failures: unavailable interactive desktop, then an incorrect helper HWND. The final run selected the real render HWND explicitly.

This portable version was syntax-checked and dry-planned after that run. Its path/provenance validation changes have not themselves been used for an additional native launch. The executed predecessor and actual run provenance remain archived privately.

## A/B alignment

For Consecration compare the original ground-effect footprint, orientation, emitter origin, particle motion and lifetime against the candidate at the same spell phase. Align hero/ground plane and cast age. Use the original spell catalog and effect-stage contract to identify the effect; retain adjacent frames and original video. Automatic level-up choices can add other spells, so the early starting-spell interval is the cleanest reference.

Do not apply whole-frame SSIM as a visual acceptance criterion: terrain, camera, UI, lighting, enemy population and engine rendering differ. First verify the intended source stage and active emitter window, then compare a grounded world-effect region. Broad golden ground fire versus a vertical column is a meaningful failure even if both renders contain nonzero pixels.

## Git boundary

Track only this README and `record_original.py`. Keep all run directories, original EXE/MPQs, shader caches, client profiles, screenshots, videos, logs and disassembly evidence private and ignored. No source-game asset or decompiled code is included here. Do not publish user-owned assets with the mod's source repository.
