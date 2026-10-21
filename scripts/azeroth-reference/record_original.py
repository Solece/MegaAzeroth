"""Capture the owned original Azeroth client using its native automation and UM.

No UI input, activation, game hooks, custom renderer or custom recorder.
Without --launch this prints a plan and does not start or copy the game.
"""
from pathlib import Path
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time

EXPECTED_EXE_SHA256 = "2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a"
SOURCE_ARGS = ["elwynn", "paladin", "human"]
NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)


def digest(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def save(path, data):
    Path(path).write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def under(path, root):
    return path == root or root in path.parents


def powershell(script):
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", script],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
        creationflags=NO_WINDOW, timeout=12)
    if result.returncode:
        raise RuntimeError(result.stderr[-1500:])
    return result.stdout.strip()


def original_pids():
    value = powershell("@(Get-Process | Where-Object {$_.ProcessName -eq 'AzerothSurvivors'} | Select-Object -ExpandProperty Id) | ConvertTo-Json -Compress")
    if not value:
        return []
    parsed = json.loads(value)
    return parsed if isinstance(parsed, list) else [parsed]


def foreground():
    user = ctypes.windll.user32
    user.GetForegroundWindow.restype = ctypes.c_void_p
    user.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.DWORD)]
    hwnd = user.GetForegroundWindow()
    pid = wintypes.DWORD()
    user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return {"Hwnd": int(hwnd or 0), "Pid": pid.value}


def owned_windows(pid):
    """Read-only HWND inventory; excludes helper windows when selecting capture."""
    user = ctypes.windll.user32
    rows = []
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.IsWindowVisible.argtypes = [wintypes.HWND]
    user.IsIconic.argtypes = [wintypes.HWND]
    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    get_style = user.GetWindowLongPtrW
    get_style.argtypes = [wintypes.HWND, ctypes.c_int]
    get_style.restype = ctypes.c_ssize_t

    def observe(hwnd, _):
        owner = wintypes.DWORD()
        user.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
        if owner.value != pid:
            return True
        rect = wintypes.RECT()
        user.GetClientRect(hwnd, ctypes.byref(rect))
        title = ctypes.create_unicode_buffer(max(1, user.GetWindowTextLengthW(hwnd) + 1))
        cls = ctypes.create_unicode_buffer(256)
        user.GetWindowTextW(hwnd, title, len(title))
        user.GetClassNameW(hwnd, cls, len(cls))
        rows.append({"Hwnd": int(hwnd), "Pid": pid, "Title": title.value, "Class": cls.value,
                     "ClientWidth": rect.right - rect.left, "ClientHeight": rect.bottom - rect.top,
                     "Visible": bool(user.IsWindowVisible(hwnd)), "Minimized": bool(user.IsIconic(hwnd)),
                     "ToolWindow": bool(get_style(hwnd, -20) & 0x80)})
        return True

    user.EnumWindows(callback_type(observe), 0)
    return rows


def process_telemetry(pid):
    value = powershell(f"$p=Get-Process -Id {pid} -ErrorAction SilentlyContinue; if($p){{$p.Refresh();[pscustomobject]@{{Pid=$p.Id;CPUSeconds=$p.CPU;PrivateBytes=$p.PrivateMemorySize64;MainHwnd=$p.MainWindowHandle.ToInt64()}} | ConvertTo-Json -Compress}}")
    return json.loads(value) if value else None


def source_files(install):
    exe = install / "AzerothSurvivors.exe"
    mpqs = sorted((install / "Data").glob("*.MPQ"))
    if not exe.is_file() or not mpqs:
        raise RuntimeError("Install must contain AzerothSurvivors.exe and Data/*.MPQ")
    files = [exe, *mpqs]
    rows = [{"Path": str(p), "Relative": p.relative_to(install).as_posix(),
             "Bytes": p.stat().st_size, "MtimeNs": p.stat().st_mtime_ns,
             "Sha256": digest(p)} for p in files]
    if rows[0]["Sha256"] != EXPECTED_EXE_SHA256:
        raise RuntimeError("Executable version is unverified; expected the pinned source SHA256")
    return rows


def ensure_owned_copy(runtime, rows, work):
    """Physical copies only: links could permit writeback into the source install."""
    needed = sum(r["Bytes"] for r in rows if not (runtime / r["Relative"]).exists())
    if shutil.disk_usage(work).free < needed + 1024 ** 3:
        raise RuntimeError("Insufficient space for a physical private copy")
    for row in rows:
        dest = runtime / row["Relative"]
        if not under(dest.resolve(), work):
            raise RuntimeError("Owned runtime resolves outside the selected work directory")
        if not dest.exists():
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(row["Path"], dest)
        if os.path.samefile(row["Path"], dest) or dest.stat().st_nlink > 1:
            raise RuntimeError("Private runtime must contain physical files, not hardlinks")
        if dest.stat().st_size != row["Bytes"] or digest(dest) != row["Sha256"]:
            raise RuntimeError("Private runtime differs from source: " + row["Relative"])


def png_info(path):
    with path.open("rb") as stream:
        header = stream.read(24)
    valid = header[:8] == b"\x89PNG\r\n\x1a\n" and header[12:16] == b"IHDR"
    width, height = struct.unpack(">II", header[16:24]) if valid else (0, 0)
    return {"Path": str(path), "Bytes": path.stat().st_size, "Sha256": digest(path),
            "Width": width, "Height": height, "ValidHeader": valid and width > 0 and height > 0}


def validate_video(path, ffmpeg):
    """Decode with the existing FFmpeg; a nonempty file alone is insufficient."""
    result = subprocess.run([str(ffmpeg), "-hide_banner", "-nostdin", "-i", str(path),
                             "-f", "null", "-"], capture_output=True, text=True,
                            errors="replace", creationflags=NO_WINDOW, timeout=45)
    log = path.with_suffix(".decode.log")
    log.write_text(result.stderr, encoding="utf-8")
    probe = ffmpeg.with_name("ffprobe.exe")
    metadata = None
    if probe.exists():
        check = subprocess.run([str(probe), "-v", "error", "-count_frames", "-show_streams",
                                "-show_format", "-of", "json", str(path)], capture_output=True,
                               text=True, errors="replace", creationflags=NO_WINDOW, timeout=45)
        if check.returncode == 0:
            metadata = json.loads(check.stdout)
    counts = re.findall(r"frame=\s*(\d+)", result.stderr)
    decoded = int(counts[-1]) if counts else 0
    return {"Path": str(path), "Bytes": path.stat().st_size, "Sha256": digest(path),
            "DecodeExitCode": result.returncode, "DecodedFrames": decoded,
            "DecodeLog": str(log), "Metadata": metadata}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--install", type=Path, required=True, help="Owned original install, read only")
    default_work = os.environ.get("AZEROTH_REFERENCE_WORK")
    parser.add_argument("--work-root", type=Path, default=Path(default_work) if default_work else None,
                        help="Private runtime/profile/output root; or AZEROTH_REFERENCE_WORK")
    parser.add_argument("--ffmpeg", type=Path, default=os.environ.get("UM_FFMPEG_WIN"),
                        help="Existing FFmpeg with gfxcapture; or UM_FFMPEG_WIN")
    parser.add_argument("--seconds", type=float, default=120, help="Owned game deadline, 5..120 seconds")
    parser.add_argument("--launch", action="store_true", help="Actually start the original; otherwise print plan")
    parser.add_argument("--record", action="store_true", help="Also call installed um win record")
    args = parser.parse_args()
    if args.work_root is None:
        parser.error("--work-root or AZEROTH_REFERENCE_WORK is required")
    if not 5 <= args.seconds <= 120:
        parser.error("--seconds must be between 5 and 120")
    install = args.install.resolve()
    work = args.work_root.resolve()
    if under(work, install) or under(install, work):
        raise RuntimeError("Private work directory must not overlap the original install")
    runtime = work / "owned-runtime"
    profile = work / "isolated-profile"
    if not under(runtime.resolve(), work) or not under(profile.resolve(), work):
        raise RuntimeError("Runtime/profile must resolve under the private work directory")
    run = work / ("run-paladin-" + time.strftime("%Y%m%d-%H%M%S"))
    shots = run / "native-frames"
    ffmpeg = Path(args.ffmpeg).resolve() if args.ffmpeg else None
    overrides = {
        "WOW_UNATTENDED": "1", "BENILLA_HOME": str(profile / "client"),
        "WOW_ROGUE_SAVE": str(profile / "characters.txt"), "WOW_ROGUE_AUTOSTART": "1",
        "WOW_ROGUE_AUTOPICK": "1", "WOW_ROGUE_AUTOPLAY": "3",
        "WOW_ROGUE_AUTOLOGOUT": str(max(25, int(args.seconds - 10))),
        "WOW_ROGUE_SHOTS": str(shots), "WOW_ROGUE_UPDATE": "0", "WOW_DATA": str(runtime / "Data"),
        "APPDATA": str(profile / "appdata-roaming"), "LOCALAPPDATA": str(profile / "appdata-local"),
        "USERPROFILE": str(profile / "user"), "HOME": str(profile / "user"),
        "XDG_DATA_HOME": str(profile / "data"), "TEMP": str(profile / "temp"), "TMP": str(profile / "temp")}
    plan = {"OriginalExe": str(install / "AzerothSurvivors.exe"), "OriginalExeSha256": EXPECTED_EXE_SHA256,
            "Command": [str(runtime / "AzerothSurvivors.exe"), *SOURCE_ARGS], "WorkingDirectory": str(runtime),
            "EnvironmentOverrides": overrides, "RemovedInheritedEnvironment": ["WOW_*", "BENILLA_HOME"],
            "WOW_CAPTURE": "UNSET: bypasses BENILLA_HOME when present", "WOW_ROGUE_SHOWCASE": "UNSET: unverified",
            "PhysicalDataCopy": True, "ReuseOwnedRuntimeAndPrivateShaderCache": True,
            "DeadlineSeconds": args.seconds, "LaunchRequested": args.launch,
            "Recorder": "installed um win record; exact owned HWND/PID; no audio", "FFmpeg": str(ffmpeg) if ffmpeg else None,
            "Python": sys.executable, "RunnerSha256": digest(__file__),
            "FocusPolicy": "No input/activation. Abort own process if it becomes foreground; do not restore focus."}
    print(json.dumps(plan, indent=2), flush=True)
    if not args.launch:
        return 0
    if os.name != "nt":
        raise RuntimeError("Windows only")
    if args.record:
        if ffmpeg is None or not ffmpeg.is_file():
            raise RuntimeError("--record requires an existing --ffmpeg / UM_FFMPEG_WIN executable")
        import importlib.util
        if importlib.util.find_spec("um") is None:
            raise RuntimeError("Run using the existing Python environment with Universal Modder installed")
    if original_pids():
        raise RuntimeError("An original Azeroth process already exists; refusing another launch")
    initial = foreground()
    if not initial["Hwnd"]:
        raise RuntimeError("Interactive desktop foreground is unavailable; refuse an unobservable launch")
    work.mkdir(parents=True, exist_ok=True)
    before = source_files(install)
    ensure_owned_copy(runtime, before, work)
    run.mkdir()
    shots.mkdir()
    for name in ("client", "appdata-roaming", "appdata-local", "user", "data", "temp", "recorder-local"):
        (profile / name).mkdir(parents=True, exist_ok=True)
    shutil.copy2(__file__, run / "runner-source.py")
    save(run / "source-files-before.json", before)
    plan["ForegroundBefore"] = initial
    save(run / "preflight.json", plan)
    env = {k: v for k, v in os.environ.items() if not k.startswith("WOW_") and k != "BENILLA_HOME"}
    env.update(overrides)
    start_info = subprocess.STARTUPINFO()
    start_info.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    start_info.wShowWindow = 0
    proc = None
    recorder = None
    recorder_stream = None
    capture_rows = []
    events = [{"Elapsed": 0, **initial}]
    telemetry = []
    failure = None
    stole = False
    window = 0
    last_windows = []
    launch_time = time.monotonic()
    try:
        with (run / "original.log").open("wb") as log:
            proc = subprocess.Popen(plan["Command"], cwd=runtime, env=env, stdout=log,
                                    stderr=subprocess.STDOUT, startupinfo=start_info, creationflags=NO_WINDOW)
            plan["OwnedPid"] = proc.pid
            save(run / "launch.json", plan)
            print("OWNED_ORIGINAL_PID", proc.pid, "RUN", run, flush=True)
            deadline = launch_time + args.seconds
            next_window = next_telemetry = launch_time
            while proc.poll() is None and time.monotonic() < deadline:
                elapsed = time.monotonic() - launch_time
                current = foreground()
                if current != {k: events[-1][k] for k in ("Hwnd", "Pid")}:
                    events.append({"Elapsed": round(elapsed, 3), **current})
                if current["Pid"] == proc.pid:
                    stole = True
                    failure = "Original activated itself; stopped without input or focus restoration"
                    break
                if time.monotonic() >= next_window:
                    last_windows = owned_windows(proc.pid)
                    next_window = time.monotonic() + 2
                    eligible = [w for w in last_windows if w["ClientWidth"] >= 320 and w["ClientHeight"] >= 180
                                and w["Visible"] and not w["Minimized"] and not w["ToolWindow"]
                                and w["Title"] == "Azeroth Survivors" and w["Class"] == "Window Class"]
                    selected = max(eligible, key=lambda w: (w["Visible"], w["ClientWidth"] * w["ClientHeight"]), default=None)
                    if selected:
                        window = selected["Hwnd"]
                    frames_exist = next(shots.glob("*.png"), None) is not None
                    retry_ready = not capture_rows or (len(capture_rows) == 1 and frames_exist and elapsed >= 70)
                    idle = recorder is None or recorder.poll() is not None
                    if window and args.record and idle and retry_ready and (frames_exist or elapsed >= 35) and deadline - time.monotonic() > 5:
                        if recorder is not None:
                            capture_rows[-1]["ExitCode"] = recorder.returncode
                        if recorder_stream is not None:
                            recorder_stream.close()
                        number = len(capture_rows) + 1
                        remaining = round(max(1, min(30, deadline - time.monotonic() - 2)), 2)
                        capture_env = os.environ.copy()
                        capture_env.update({"UM_FFMPEG_WIN": str(ffmpeg), "LOCALAPPDATA": str(profile / "recorder-local")})
                        command = [sys.executable, "-m", "um", "win", "record", "--hwnd", str(window), "--pid", str(proc.pid),
                                   "--out", str(run / f"original-paladin-{number:02d}"), "--seconds", str(remaining),
                                   "--fps", "30", "--no-audio"]
                        recorder_stream = (run / "um-record.log").open("ab")
                        recorder = subprocess.Popen(command, cwd=work, env=capture_env, stdout=recorder_stream,
                                                    stderr=subprocess.STDOUT, creationflags=NO_WINDOW)
                        capture_rows.append({"Command": command, "Pid": recorder.pid, "Hwnd": window,
                                             "ElapsedAtStart": round(elapsed, 3), "ExitCode": None})
                        save(run / "recorder-commands.json", capture_rows)
                        print("UM_RECORDER_PID", recorder.pid, "HWND", window, flush=True)
                if time.monotonic() >= next_telemetry:
                    row = {"Elapsed": round(elapsed, 2), "Process": process_telemetry(proc.pid),
                           "Windows": last_windows, "NativeFrames": len(list(shots.glob("*.png")))}
                    telemetry.append(row)
                    save(run / "loading-telemetry.json", telemetry)
                    next_telemetry = time.monotonic() + 10
                    print("ORIGINAL_LOADING", json.dumps({k: row[k] for k in ("Elapsed", "Process", "NativeFrames")}), flush=True)
                time.sleep(.2)
    except Exception as ex:
        failure = repr(ex)
    finally:
        stopped = proc is not None and proc.poll() is None
        if stopped:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=5)
        if recorder is not None:
            try:
                recorder.wait(timeout=25)
            except subprocess.TimeoutExpired:
                failure = (failure + "; " if failure else "") + "UM exceeded its own recording deadline"
                recorder.terminate()
                recorder.wait(timeout=5)
            capture_rows[-1]["ExitCode"] = recorder.returncode
        if recorder_stream is not None:
            recorder_stream.close()
        save(run / "recorder-commands.json", capture_rows)
        after = source_files(install)
        save(run / "source-files-after.json", after)
        frames = [png_info(p) for p in sorted(shots.glob("*.png"))]
        videos = [validate_video(p, ffmpeg) for p in sorted(run.glob("original-paladin*.mkv"))
                  if p.stat().st_size > 0 and ffmpeg]
        valid_frames = any(p["ValidHeader"] for p in frames)
        valid_video = any(p["DecodeExitCode"] == 0 and p["DecodedFrames"] > 0 for p in videos)
        if not valid_frames and not valid_video:
            failure = (failure + "; " if failure else "") + "No original renderer frames/video captured"
        if before != after:
            failure = (failure + "; " if failure else "") + "Original install content/metadata changed"
        summary = {"RunDirectory": str(run), "OwnedPid": proc.pid if proc else None, "OwnedWindow": window,
                   "OriginalExitCode": proc.returncode if proc else None, "StoppedOwnProcessAtDeadline": stopped,
                   "ElapsedSeconds": round(time.monotonic() - launch_time, 3), "ForegroundBefore": initial,
                   "ForegroundAfter": foreground(), "ForegroundEvents": events, "OwnedWindowStoleFocus": stole,
                   "NativeFrames": frames, "Videos": videos, "CaptureCommands": capture_rows,
                   "CapturedAnyOriginalFrame": valid_frames or valid_video, "OriginalFilesUnchanged": before == after,
                   "UnrelatedProcessControl": "None", "PrivateProfile": str(profile), "Failure": failure,
                   "ReferenceProvenance": "Owned original executable and native renderer; no Unity adapter/software preview",
                   "Scenario": {"Class": "paladin", "Race": "human", "StartingSpell": "consecration", "SpellDbc": 26573},
                   "VisualEquivalencePassed": False, "VisualReviewRequired": True}
        save(run / "summary.json", summary)
        print("ORIGINAL_REFERENCE_SUMMARY", str(run / "summary.json"), "CAPTURED", valid_frames or valid_video,
              "FOCUS_STOLEN", stole, "FAILURE", failure, flush=True)
    return 1 if failure or stole else 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as ex:
        print("ORIGINAL_REFERENCE_ERROR", repr(ex), flush=True)
        raise SystemExit(1)
