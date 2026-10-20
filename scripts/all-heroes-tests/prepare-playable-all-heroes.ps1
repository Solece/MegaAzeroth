$ErrorActionPreference='Stop'
$labRoot='D:\Games\Saitan\AzerothMegabonkLab'
$source=Join-Path $labRoot 'private-assets\all-heroes-route-runtime\Megabonk-Lab'
$parent=Join-Path $labRoot 'builds\all-heroes-v3'
$target=Join-Path $parent 'Megabonk-Lab'
if(Test-Path -LiteralPath $target){throw 'Playable candidate already exists; inspect it before changing it'}
New-Item -ItemType Directory -Path $target -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $parent 'reports') -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $target
& robocopy (Join-Path $source 'BepInEx') (Join-Path $target 'BepInEx') /E /XJ /XF LogOutput.log ErrorLog.log /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if($LASTEXITCODE -gt 7){throw ('Playable BepInEx copy failed '+$LASTEXITCODE)}
foreach($name in @('Megabonk_Data','D3D12','dotnet','AzerothAssets','AzerothMageAssets','AzerothElwynnAssets')){
  New-Item -ItemType Junction -Path (Join-Path $target $name) -Target (Join-Path $labRoot ('Megabonk-Lab\'+$name)) | Out-Null
}
# The new class pack is an independent, local user-owned asset copy.
& robocopy (Join-Path $labRoot 'private-assets\azeroth-heroes') (Join-Path $target 'AzerothHeroAssets') /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if($LASTEXITCODE -gt 7){throw ('Original local class-pack copy failed '+$LASTEXITCODE)}
$config=Join-Path $target 'BepInEx\config\local.azerothmegabonk.abilities.cfg'
$content=Get-Content -LiteralPath $config -Raw
$content=[regex]::Replace($content,'GenericMageRuntime\s*=\s*(true|false)','GenericMageRuntime = true')
[IO.File]::WriteAllText($config,$content,[Text.UTF8Encoding]::new($false))
$ignore=Join-Path $labRoot '.gitignore'
$text=Get-Content -LiteralPath $ignore -Raw
if($text -notmatch '(?m)^builds/$'){Add-Content -LiteralPath $ignore -Encoding UTF8 -Value 'builds/'}
$plugins=Get-ChildItem -LiteralPath (Join-Path $target 'BepInEx\plugins') -Filter '*.dll' -Recurse -File | ForEach-Object {@{path=$_.FullName.Substring($target.Length+1);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}}
$assets=Get-ChildItem -LiteralPath (Join-Path $target 'AzerothHeroAssets') -Recurse -File
@{prepared=(Get-Date -Format o);runtime=$target;nativeGameBase=Join-Path $labRoot 'Megabonk-Lab';plugins=@($plugins);classAssetFiles=$assets.Count;classAssetBytes=($assets | Measure-Object -Property Length -Sum).Sum;acceptance='Pending root final matrix, native route and packaged-menu acceptance';userGameStopped=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $parent 'build-manifest.json') -Encoding UTF8
Write-Output ('Separate all-heroes playable candidate staged: '+$target)
