@echo off
setlocal
set "AZEROTH_LAB_HEADLESS_TEST="
set "AZEROTH_LAB_RENDER_PROBE="
set "AZEROTH_LAB_TALENT_PROBE="
set "AZEROTH_LAB_UI_PROBE="
set "AZEROTH_LAB_NAV_PROBE="
set "AZEROTH_LAB_ENEMY_SCALE_PROBE="
set "AZEROTH_LAB_NAV_SHORT="
set "BUILD_ROOT=%~dp0builds\all-heroes-v3"
pushd "%BUILD_ROOT%\Megabonk-Lab"
start "" "Megabonk.exe" -azeroth-test-menu -azeroth-elwynn -azeroth-talents-max -azeroth-talent-points=999 -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -logFile "%BUILD_ROOT%\reports\play-%RANDOM%-%RANDOM%.log"
popd
endlocal
