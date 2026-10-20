param([ValidateSet('class','vitals','menu','adventure')][string]$Mode='class',[ValidateSet('warrior','paladin','hunter','rogue','priest','shaman','mage','warlock','druid')][string]$Class='warrior',[switch]$GenericMage,[ValidateSet('all-heroes-test-runtime','all-heroes-route-runtime','all-heroes-playable')][string]$RuntimeName='all-heroes-test-runtime')
$ErrorActionPreference='Stop'
$labRoot='D:\Games\Saitan\AzerothMegabonkLab'
$parent=if($RuntimeName -eq 'all-heroes-playable'){Join-Path $labRoot 'builds\all-heroes-v3'}else{Join-Path $labRoot ('private-assets\'+$RuntimeName)}
$runtime=Join-Path $parent 'Megabonk-Lab'
$executable=Join-Path $runtime 'Megabonk.exe'
$exitGrace=Get-Date
while(Get-Process -Name Megabonk -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $executable}){
  if(((Get-Date)-$exitGrace).TotalSeconds -gt 10){throw 'Owned all-hero runtime is still running after exit grace'}
  Start-Sleep -Milliseconds 250
}
$config=Join-Path $runtime 'BepInEx\config\local.azerothmegabonk.abilities.cfg'
$content=Get-Content -LiteralPath $config -Raw
$enableGeneric=if($GenericMage){'true'}else{'false'}
$content=[regex]::Replace($content,'GenericMageRuntime\s*=\s*(true|false)',('GenericMageRuntime = '+$enableGeneric))
[IO.File]::WriteAllText($config,$content,[Text.UTF8Encoding]::new($false))
$env:AZEROTH_LAB_HEADLESS_TEST='1'
$env:AZEROTH_LAB_RENDER_PROBE='1'
foreach($variableName in @('AZEROTH_LAB_UI_PROBE','AZEROTH_LAB_TALENT_PROBE','AZEROTH_LAB_NAV_PROBE','AZEROTH_LAB_NAV_SHORT','AZEROTH_LAB_ENEMY_SCALE_PROBE')){[Environment]::SetEnvironmentVariable($variableName,'','Process')}
$log=Join-Path $parent ('reports\final-'+$Mode+'-'+$Class+'-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-player.log')
$arguments=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-azeroth-elwynn','-azeroth-elwynn-seed=31415','-azeroth-talents-max','-azeroth-talent-points=999','-logFile',$log)
if($Mode -eq 'menu'){$arguments+=@('-azeroth-test-menu','-azeroth-hero-menu-smoke')}
else{
  $arguments+=@('-azeroth-test-battle',('-azeroth-hero='+$Class))
  if($Mode -eq 'class'){$arguments+='-azeroth-hero-class-smoke'}
  elseif($Mode -eq 'vitals'){$arguments+='-azeroth-vitals-smoke'}
  elseif($Mode -eq 'adventure'){$arguments+=@('-azeroth-adventure-smoke','-azeroth-visual-smoke')}
}
if($GenericMage){$arguments+='-azeroth-generic-mage-probe'}
$owned=Start-Process -FilePath $executable -WorkingDirectory $runtime -ArgumentList $arguments -WindowStyle Hidden -PassThru
@{pid=$owned.Id;class=$Class;mode=$Mode;genericMage=[bool]$GenericMage;executable=$executable;log=$log;started=(Get-Date -Format o)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $parent 'reports\owned-final-probe.json') -Encoding UTF8
Write-Output ('Final native '+$Mode+' '+$Class+' PID='+$owned.Id+'; genericMage='+[bool]$GenericMage+'; log='+$log)
