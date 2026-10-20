$ErrorActionPreference='Stop'
$labRoot='D:\Games\Saitan\AzerothMegabonkLab'
$parent=Join-Path $labRoot 'private-assets\all-heroes-test-runtime'
$previous=Join-Path $parent 'reports\final-class-matrix.json'
$results=@()
if(Test-Path -LiteralPath $previous){$results=@(Get-Content -LiteralPath $previous -Raw | ConvertFrom-Json | Where-Object {$_.pass})}
foreach($className in @('druid','hunter','warrior','paladin','rogue','priest','shaman','mage','warlock')){
  if($results | Where-Object {$_.class -eq $className -and $_.pass}){continue}
  & (Join-Path $PSScriptRoot 'run-final-class-scenario.ps1') -Mode class -Class $className -GenericMage:($className -eq 'mage')
  $owned=Get-Content -LiteralPath (Join-Path $parent 'reports\owned-final-probe.json') -Raw | ConvertFrom-Json
  $began=Get-Date
  while($true){
    $child=Get-Process -Id $owned.pid -ErrorAction SilentlyContinue
    if(-not $child -or $child.HasExited){break}
    if(((Get-Date)-$began).TotalSeconds -gt 140){
      if($child.Path -eq $owned.executable){Stop-Process -Id $child.Id -Force}
      throw ('Exact owned probe deadline exceeded: '+$className+' PID='+$owned.pid)
    }
    Start-Sleep -Seconds 1
  }
  $summary=Join-Path $parent ('reports\hero-class-'+$owned.pid+'\summary.json')
  if(-not (Test-Path -LiteralPath $summary)){throw ('Missing native result: '+$owned.log)}
  $result=Get-Content -LiteralPath $summary -Raw | ConvertFrom-Json
  $results+=@{class=$className;pid=$owned.pid;pass=$result.pass;passed=$result.passed;failed=$result.failed;skipped=$result.skipped;wallSeconds=$result.wallSeconds;summary=$summary;genericMage=[bool]$owned.genericMage}
  $results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $parent 'reports\final-class-matrix.json') -Encoding UTF8
  Write-Output ('FINAL CLASS '+$className+' pass='+$result.pass+' checks='+$result.passed+' fail='+$result.failed+' skip='+$result.skipped)
  if(-not $result.pass){throw ('Native class failed; retained evidence: '+$summary)}
}
Write-Output 'Final nine-class native matrix completed.'
