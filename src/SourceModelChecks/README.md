# Source catalog checks

Run with `dotnet`, not a Windows apphost. The entry point catches validation failures, writes the error to stderr and returns1. It never launches the game or writes game saves.

```powershell
dotnet build src/SourceModelChecks/SourceModelChecks.csproj -c Release
dotnet src/SourceModelChecks/bin/Release/net6.0/SourceModelChecks.dll --catalogs src/MageTalents/catalogs --output reports/source-model-checks
dotnet src/SourceModelChecks/bin/Release/net6.0/SourceModelChecks.dll --verify-handled-failure
```

The last command intentionally returns1 and logs a controlled exception. Catalog validation checks original ranks, source filters, normal51-point progression and the explicitly authorized999-point debug mode. These are data/allocation checks, not native combat proof.
