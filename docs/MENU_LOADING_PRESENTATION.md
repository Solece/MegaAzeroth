# Menu and loading presentation

`AzerothPresentation` is a separate eleventh plugin. The ten accepted gameplay assemblies from v5 remain byte-identical.

The native main menu uses the converted original Eastern Kingdom artwork, source tint `(0.40, 0.38, 0.36, 1)` and 106% overscan. The original artwork is also used during native map loading, with the genuine loading-bar border and fill. Source provenance and reproducible export instructions are in `MENU_LOADING_SOURCE.md`. Vignette geometry and loading-bar placement are adaptations; the original loading-screen layout was not visually recorded in this audit.

The menu backdrop sits behind the actual selected hero. Only identified native menu scenery, terrain, grass, ambient Ent renderers and wind are masked. Only the DepthOfField effect's active flag is disabled, with its previous value restored when leaving the menu. The camera, hero, controls, movement, lighting and audio retain their native behavior. Destroyed transient renderer references are pruned before they are used or diagnosed.

The loading overlay is a child of the actual LoadingScreen. It reads the existing native loading-bar transform, never writes native progress, never delays activation and intercepts no input. Unique native Start/OnDisable/OnDestroy hooks cover attachment and cleanup; shared empty IL2CPP stubs are not patched.

## Native verification

The batch-only fixture records the real main menu, character picker, map picker, native loading, gameplay, pause/resume, second stage, results and return to menu. The native next-stage call is an explicit lifecycle fixture, not a claim that a natural boss/portal progression was completed. A separate existing nine-hero picker test checks every class with the accepted plugin.

Captures render the actual native camera and temporarily transport the actual Canvas tree to an offscreen camera, restoring state in `finally`. They are not OS-window screenshots or pixel-equivalence proof against the source engine. Same-frame hero and loading-fill visibility pairs verify that actual objects contribute pixels. Fill width and UV fractions are compared with the native loading progress. Quick transitions may naturally skip some progress thresholds.

The fixture waits on actual CanvasRenderer transition opacity, including inherited alpha and render visibility. Graphic.color alone does not expose the native CrossFadeAlpha tween. Probe input isolation, captures and scripted scene changes are armed only by batch mode, two explicit environment flags and the presentation smoke flag; they are inactive during normal play.

Failed candidates, native logs, phase heartbeats and captured frames remain in the development QA reports. Final acceptance, hashes and check counts are recorded in MODLOG.md and build-info.json. Passing these checks does not establish full visual equivalence for all gameplay effects.
