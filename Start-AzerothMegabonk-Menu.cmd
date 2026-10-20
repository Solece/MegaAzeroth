@echo off
setlocal
set "AZEROTH_LAB_HEADLESS_TEST="
set "AZEROTH_LAB_RENDER_PROBE="
pushd "%~dp0Megabonk-Lab"
start "" "Megabonk.exe" -logFile "%~dp0reports\megabonk-menu-%RANDOM%-%RANDOM%.log"
popd
endlocal
