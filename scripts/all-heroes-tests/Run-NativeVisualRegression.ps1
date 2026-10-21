param(
 [ValidateSet('cat','targeting','paladin','variety')][string]$Mode='paladin',
 [string]$LabRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
 [string]$RuntimeParent='',
 [switch]$InstallCandidate
)
$ErrorActionPreference='Stop'
$lab=[IO.Path]::GetFullPath($LabRoot)
if(!$RuntimeParent){$RuntimeParent=Join-Path $lab 'private-assets\vfx-fidelity-runtime'}
$parent=[IO.Path]::GetFullPath($RuntimeParent)
$runtime=Join-Path $parent 'Megabonk-Lab'
$exe=Join-Path $runtime 'Megabonk.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Prepare the isolated local runtime first'}
if(Get-Process -Name Megabonk -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $exe}){throw 'Selected runtime is active; no existing process will be stopped'}
if($InstallCandidate){
 foreach($m in @(@('MageVisuals','AzerothMegabonk.MageVisuals.dll','MageVisuals'),@('AzerothAbilities','AzerothAbilities.dll','AzerothAbilities'),@('ElwynnBiome','AzerothMegabonk.ElwynnBiome.dll','ElwynnBiome'),@('EnemyVisuals','AzerothMegabonk.EnemyVisuals.dll','AzerothMegabonk'))){
  Copy-Item -LiteralPath (Join-Path $lab ('src\'+$m[0]+'\bin\Release\net6.0\'+$m[1])) -Destination (Join-Path $runtime ('BepInEx\plugins\'+$m[2]+'\'+$m[1])) -Force
 }
 $fixture=Join-Path $runtime 'BepInEx\plugins\AzerothPaladinVfxProbe'
 New-Item -ItemType Directory -Path $fixture -Force|Out-Null
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\vfx-validation\consecration-contract.json') -Destination $fixture -Force
}
$env:AZEROTH_LAB_HEADLESS_TEST='1';$env:AZEROTH_LAB_RENDER_PROBE='1'
foreach($key in @('AZEROTH_LAB_UI_PROBE','AZEROTH_LAB_TALENT_PROBE','AZEROTH_LAB_NAV_PROBE','AZEROTH_LAB_NAV_SHORT','AZEROTH_LAB_ENEMY_SCALE_PROBE')){[Environment]::SetEnvironmentVariable($key,'','Process')}
$hero=switch($Mode){'cat'{'druid'};'targeting'{'mage'};'paladin'{'paladin'};default{'mage'}}
$flag=switch($Mode){'cat'{'-azeroth-cat-material-smoke'};'targeting'{'-azeroth-targeting-smoke'};'paladin'{'-azeroth-paladin-vfx-smoke'};default{'-azeroth-enemy-variety-smoke'}}
$report=Join-Path $parent 'reports'
New-Item -ItemType Directory -Path $report -Force|Out-Null
$log=Join-Path $report ($Mode+'-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-player.log')
$argsList=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-azeroth-elwynn','-azeroth-test-battle',('-azeroth-hero='+$hero),'-azeroth-talents-max','-azeroth-talent-points=999',$flag,'-logFile',$log)
$owned=Start-Process -FilePath $exe -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -ArgumentList $argsList
@{pid=$owned.Id;mode=$Mode;executable=$exe;log=$log;started=(Get-Date -Format o);sourceEngineVisualEquivalence='UNVERIFIED until actual A/B review'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $report 'owned-native-regression.json') -Encoding UTF8
Write-Output ('Isolated native '+$Mode+' check PID='+$owned.Id+' log='+$log)
Write-Output 'No foreground activation or input automation is performed. Inspect the PID-specific native summary after exit.'
