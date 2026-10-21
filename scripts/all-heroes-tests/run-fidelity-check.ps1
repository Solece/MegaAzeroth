param(
 [ValidateSet('vfx','ghost','targeting','menu')][string]$Mode='vfx',
 [ValidateSet('warrior','paladin','hunter','rogue','priest','shaman','mage','warlock','druid')][string]$Class='warlock',
 [string]$RuntimeParent='',
 [string]$Spell='',
 [switch]$Death,[switch]$Shield,[switch]$Budget,[switch]$DensityTest
)
$ErrorActionPreference='Stop'
$lab=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(!$RuntimeParent){$RuntimeParent=Join-Path $lab 'private-assets\vfx-fidelity-runtime'}
$parent=[IO.Path]::GetFullPath($RuntimeParent)
$runtime=Join-Path $parent 'Megabonk-Lab'
$exe=Join-Path $runtime 'Megabonk.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Prepare an isolated local runtime first'}
if(Get-Process -Name Megabonk -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe}){throw 'Selected runtime is already running; no game will be stopped'}
$env:AZEROTH_LAB_HEADLESS_TEST='1';$env:AZEROTH_LAB_RENDER_PROBE='1'
foreach($key in @('AZEROTH_LAB_UI_PROBE','AZEROTH_LAB_TALENT_PROBE','AZEROTH_LAB_NAV_PROBE','AZEROTH_LAB_NAV_SHORT','AZEROTH_LAB_ENEMY_SCALE_PROBE')){[Environment]::SetEnvironmentVariable($key,'','Process')}
$report=Join-Path $parent 'reports'
New-Item -ItemType Directory -Path $report -Force|Out-Null
$log=Join-Path $report ('fidelity-'+$Mode+'-'+$Class+'-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-player.log')
$argsList=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-azeroth-elwynn','-azeroth-talents-max','-azeroth-talent-points=999','-logFile',$log)
if($Mode -eq 'menu'){$argsList+=@('-azeroth-test-menu','-azeroth-hero-menu-smoke')}
else{
 $argsList+=@('-azeroth-test-battle',('-azeroth-hero='+$Class))
 if($Mode -eq 'vfx'){
  $argsList+='-azeroth-vfx-fidelity-smoke'
  if($Spell){$argsList+=('-azeroth-vfx-spell='+$Spell)}
  if($Death){$argsList+='-azeroth-vfx-death-smoke'}
  if($Shield){$argsList+='-azeroth-vfx-shield-smoke'}
  if($Budget){$argsList+='-azeroth-vfx-budget-smoke'}
 }
 elseif($Mode -eq 'ghost'){$argsList+='-azeroth-ghost-smoke'}
 elseif($Mode -eq 'targeting'){$argsList+='-azeroth-targeting-smoke'}
}
if($DensityTest){$argsList+='-azeroth-decor-density=1.2'}
$owned=Start-Process -FilePath $exe -WorkingDirectory $runtime -WindowStyle Hidden -PassThru -ArgumentList $argsList
@{pid=$owned.Id;executable=$exe;mode=$Mode;class=$Class;log=$log;started=(Get-Date -Format o)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $report 'owned-fidelity-check.json') -Encoding UTF8
Write-Output ('Independent '+$Mode+' check PID='+$owned.Id+'; native reports in '+$report)
