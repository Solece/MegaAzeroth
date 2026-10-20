$ErrorActionPreference='Stop'
$labRoot='D:\Games\Saitan\AzerothMegabonkLab'
$parent=Join-Path $labRoot 'private-assets\all-heroes-route-runtime'
$results=@()
foreach($className in @('druid','paladin','priest')){
  & (Join-Path $PSScriptRoot 'run-final-class-scenario.ps1') -Mode vitals -Class $className -RuntimeName all-heroes-route-runtime -GenericMage
  $owned=Get-Content -LiteralPath (Join-Path $parent 'reports\owned-final-probe.json') -Raw | ConvertFrom-Json
  $began=Get-Date
  while(Get-Process -Id $owned.pid -ErrorAction SilentlyContinue){
    if(((Get-Date)-$began).TotalSeconds -gt 140){throw ('Vitals exact owned process deadline: '+$owned.pid)}
    Start-Sleep -Seconds 1
  }
  $path=Join-Path $parent ('reports\vitals-'+$owned.pid+'\summary.json')
  $s=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  $results+=@{class=$className;pid=$owned.pid;pass=$s.pass;passed=$s.passed;failed=$s.failed;skipped=$s.skipped;summary=$path}
  $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $parent 'reports\final-vitals-summary.json') -Encoding UTF8
  Write-Output ('FINAL VITALS '+$className+' pass='+$s.pass+' fail='+$s.failed)
  if(-not $s.pass){throw ('Vitals failed: '+$path)}
}
$previous=Join-Path $parent 'reports\adventure-smoke.log'
if(Test-Path -LiteralPath $previous){Copy-Item -LiteralPath $previous -Destination (Join-Path $parent 'reports\adventure-F3-warlock-6820.log') -Force}
& (Join-Path $PSScriptRoot 'run-final-class-scenario.ps1') -Mode adventure -Class warlock -RuntimeName all-heroes-route-runtime -GenericMage
Write-Output 'Final native route started; inspect exact PID report before acceptance.'
