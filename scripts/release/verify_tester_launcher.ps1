#Requires -Version 5.1
param([string]$LauncherRoot=(Join-Path $PSScriptRoot '../..'),[string]$OutputRoot=(Join-Path ([IO.Path]::GetTempPath()) 'MegaAzeroth-launcher-checks'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fixture=Join-Path $OutputRoot ('checks/other PC '+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $fixture 'Runtime/BepInEx'))|Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'reports'))|Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'saves'))|Out-Null
foreach($name in @('Play.ps1','Collect-Logs.ps1')){Copy-Item -LiteralPath (Join-Path $LauncherRoot $name) -Destination (Join-Path $fixture $name)}
[IO.File]::WriteAllText((Join-Path $fixture '.env'),'PRIVATE_SENTINEL_NOT_FOR_DIAGNOSTICS')
[IO.File]::WriteAllText((Join-Path $fixture 'saves/player.json'),'PRIVATE_SAVE_NOT_FOR_DIAGNOSTICS')
[IO.File]::WriteAllText((Join-Path $fixture 'Runtime/BepInEx/LogOutput.log'),('Q'*3MB)+"`n"+$fixture+"`n"+$env:USERPROFILE)
[IO.File]::WriteAllText((Join-Path $fixture 'reports/play-test-fixture.log'),'REAL_PLAY_LOG')
[IO.File]::WriteAllText((Join-Path $fixture 'release.json'),'{"ReleaseId":"fixture"}')
$ps51=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$results=New-Object 'System.Collections.Generic.List[object]'
function Check([bool]$Value,[string]$Name){$results.Add([pscustomobject]@{Pass=$Value;Name=$Name});if(!$Value){throw $Name}}
function Read-ZipText($Zip,[string]$Name){$entry=$Zip.GetEntry($Name);if(!$entry){throw ('Missing ZIP entry '+$Name)};$reader=New-Object IO.StreamReader ($entry.Open());try{return $reader.ReadToEnd()}finally{$reader.Dispose()}}
$output=& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Play.ps1') -CollectLogs 2>&1
Check ($LASTEXITCODE -eq 0) 'CLI collection works without launcher core or a complete runtime'
$archive=Get-ChildItem -LiteralPath (Join-Path $fixture 'reports') -Filter '*.zip'|Select-Object -First 1
Check ($null -ne $archive) 'Creates an actual ZIP from another path with spaces'
$zip=[IO.Compression.ZipFile]::OpenRead($archive.FullName)
try {
    $names=@($zip.Entries|ForEach-Object FullName)
    Check ($names -contains 'reports/play-test-fixture.log' -and $names -contains 'Runtime/BepInEx/LogOutput.log') 'Includes actual launcher and BepInEx logs'
    $all=($zip.Entries|ForEach-Object {Read-ZipText $zip $_.FullName}) -join "`n"
    Check (!$all.Contains('PRIVATE_SENTINEL_NOT_FOR_DIAGNOSTICS') -and !$all.Contains('PRIVATE_SAVE_NOT_FOR_DIAGNOSTICS')) 'Excludes unrelated secrets and player saves'
    $log=Read-ZipText $zip 'Runtime/BepInEx/LogOutput.log'
    Check ($zip.GetEntry('Runtime/BepInEx/LogOutput.log').Length -le 2MB) 'Bounds oversized logs to final two MiB'
    Check (!$log.Contains($fixture) -and !$log.Contains($env:USERPROFILE) -and $log.Contains('%PACKAGE_ROOT%')) 'Redacts package and user-profile paths'
    $system=Read-ZipText $zip 'system.json'|ConvertFrom-Json
    Check ($system.PowerShell -like '5.1*') 'Runs under actual Windows PowerShell5.1'
}finally{$zip.Dispose()}
$oldPreference=$ErrorActionPreference
try{$ErrorActionPreference='Continue';$failureOutput=& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Play.ps1') -Verify 2>&1}finally{$ErrorActionPreference=$oldPreference}
Check ($LASTEXITCODE -eq 1 -and @(Get-ChildItem -LiteralPath (Join-Path $fixture 'reports') -Filter 'launcher-error-*.txt').Count -eq 1) 'Failed verification records error and returns failure'
$again=& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'Play.ps1') -CollectLogs 2>&1
Check ($LASTEXITCODE -eq 0) 'Diagnostic collector still works after a launcher failure'
$last=Get-ChildItem -LiteralPath (Join-Path $fixture 'reports') -Filter '*.zip'|Sort-Object LastWriteTime -Descending|Select-Object -First 1
$zip=[IO.Compression.ZipFile]::OpenRead($last.FullName)
try{Check (@($zip.Entries|Where-Object FullName -like 'reports/launcher-error-*.txt').Count -eq 1) 'Diagnostic ZIP includes the recorded startup error'}finally{$zip.Dispose()}
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=$ps51
$start.Arguments='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $fixture 'Play.ps1')+'"'
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$process=[Diagnostics.Process]::Start($start)
$process.StandardInput.WriteLine('3');$process.StandardInput.WriteLine('');$process.StandardInput.Close()
if(!$process.WaitForExit(30000)){throw 'Interactive collection exceeded30s'}
Check ($process.ExitCode -eq 0) 'Interactive menu3 collects logs before any game integrity check'
$report=@{Passed=$true;Checks=$results;PowerShell=$PSVersionTable.PSVersion.ToString()}
[IO.File]::WriteAllText((Join-Path $OutputRoot 'launcher-checks.json'),($report|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Passed=$true;Checks=$results.Count;Failures=0}|ConvertTo-Json
