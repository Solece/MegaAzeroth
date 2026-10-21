# Production particle math regression

From the mod repository:

```powershell
.\scripts\all-heroes-tests\Run-ParticleRegression.ps1
```

The existing .NET10 SDK must be installed. The runner reads the **current** `src/AzerothAbilities/SpellVfx.cs`, extracts the unique `SOURCE_PARTICLE_MATH_BEGIN` / `SOURCE_PARTICLE_MATH_END` block and compiles that actual production helper in a new report folder. No generated helper copy belongs in Git. `-LabRoot` and `-ReportFolder` can override paths; an existing report folder is rejected.

The project uses `UseAppHost=false` and is executed as `dotnet ParticleRegression.dll`. Assertions are caught and reported as a nonzero exit code and JSON; no game process or native game asset is loaded. Restore is offline, with package sources cleared in the generated report directory. Tools are not downloaded or installed.

The final PSF3 suite runs10390 assertions. It covers spherical signed angles/radii, flat Consecration0x8009, actual Trap/Shield0x100 flags, sourceZ precedence and finite degenerate fallback, positive-edge burst/truncation/rearm, pause, bounded clipping and continuous carry. It does not prove native rendering or visual equivalence.

Each report records production-source, extracted-helper, runner, test-program, project and managed-DLL SHA256 values, installed SDKs, exact managed command, logs, `result.json` and `provenance.json`. Source files are read only; build outputs remain in that new report directory. Commit only this README, the runner, `Program.cs` and the project file. Keep reports, generated helper sources, binaries and caches ignored.
