param([string]$LabRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference='Stop'
$lab=[IO.Path]::GetFullPath($LabRoot)
$source=Join-Path $lab 'private-assets\vfx-fidelity-runtime\Megabonk-Lab'
$parent=Join-Path $lab 'builds\all-heroes-v4'
$target=Join-Path $parent 'Megabonk-Lab'
if(!(Test-Path -LiteralPath (Join-Path $source 'Megabonk.exe'))){throw 'Prepared and accepted fidelity runtime is required'}
if(Test-Path -LiteralPath $parent){throw 'The v4 build already exists; no existing build will be overwritten'}
$sourceExe=Join-Path $source 'Megabonk.exe'
if(Get-Process -Name Megabonk -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $sourceExe}){throw 'Wait for the isolated acceptance runtime to finish before packaging'}
New-Item -ItemType Directory -Path $target -Force|Out-Null
New-Item -ItemType Directory -Path (Join-Path $parent 'reports') -Force|Out-Null
Get-ChildItem -LiteralPath $source -File|Copy-Item -Destination $target
foreach($name in @('BepInEx','AzerothHeroAssets','AzerothAssets')){
 & robocopy (Join-Path $source $name) (Join-Path $target $name) /E /XJ /XF LogOutput.log ErrorLog.log /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
 if($LASTEXITCODE -gt 7){throw ('Independent asset/config copy failed: '+$name+' code='+$LASTEXITCODE)}
}
# Base game and unchanged, user-owned packs are read-only shared references.
foreach($name in @('Megabonk_Data','D3D12','dotnet','AzerothMageAssets','AzerothElwynnAssets')){
 $base=Join-Path $lab ('Megabonk-Lab\'+$name)
 if(!(Test-Path -LiteralPath $base)){throw ('Missing original local dependency: '+$base)}
 New-Item -ItemType Junction -Path (Join-Path $target $name) -Target $base|Out-Null
}
$plugins=Get-ChildItem -LiteralPath (Join-Path $target 'BepInEx\plugins') -Filter '*.dll' -Recurse -File|ForEach-Object {@{path=$_.FullName.Substring($target.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}}
$assets=@(Get-ChildItem -LiteralPath (Join-Path $target 'AzerothHeroAssets') -Recurse -File)+@(Get-ChildItem -LiteralPath (Join-Path $target 'AzerothAssets') -Recurse -File)
@{prepared=(Get-Date -Format o);version='all-heroes-v4';runtime=$target;nativeGameBase=Join-Path $lab 'Megabonk-Lab';plugins=@($plugins);independentAssetFiles=$assets.Count;independentAssetBytes=($assets|Measure-Object -Property Length -Sum).Sum;testDensity=1.2;normalDensity=1;acceptance='Awaiting packaged-menu and packaged combat smoke; fidelity acceptance evidence retained separately';userGameStopped=$false}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $parent 'build-manifest.json') -Encoding UTF8
Write-Output ('Independent v4 playable prepared: '+$target)
