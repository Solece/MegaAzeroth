@echo off
setlocal
set "AZEROTH_LAB_HEADLESS_TEST="
pushd "%~dp0Megabonk-Lab"
start "" "Megabonk.exe" -logFile "%~dp0reports\megabonk-play.log"
popd
endlocal
