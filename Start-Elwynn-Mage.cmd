@echo off
setlocal
set "AZEROTH_LAB_HEADLESS_TEST="
set "AZEROTH_LAB_RENDER_PROBE="
pushd "%~dp0Megabonk-Lab"
start "" "Megabonk.exe" -azeroth-test-battle -azeroth-elwynn -azeroth-talent-points=999 -azeroth-talents-max -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "%~dp0reports\mage-battle-play-%RANDOM%-%RANDOM%.log"
popd
endlocal
