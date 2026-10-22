#Requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Test','Normal','Choose')][string]$Mode='Choose',[Alias('Verify')][switch]$VerifyOnly,[switch]$VerifyAll)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'Launcher.Core.psm1') -Force
try {
    $install=Test-BundledRuntime -PackageRoot $PSScriptRoot -VerifyAll:($VerifyAll -or $VerifyOnly)
    if ($VerifyOnly) { $install | ConvertTo-Json; exit 0 }
    if ($Mode -eq 'Choose') {
        Write-Host '1: Test menu (default): 999 points, maximum talents, session progression writes suppressed.'
        Write-Host '2: Normal progression: uses native game profile and normal saves.'
        $answer=Read-Host 'Choose 1/2; Enter starts Test'
        $Mode=if ($answer -eq '' -or $answer -eq '1') {'Test'} elseif ($answer -eq '2') {'Normal'} else {throw 'Expected 1, 2, or Enter.'}
    }
    $launch=Get-BundledLaunch -PackageRoot $PSScriptRoot -RuntimeRoot $install.RuntimeRoot -Mode $Mode
    foreach ($process in @(Get-Process -Name Megabonk -ErrorAction SilentlyContinue)) {
        try { $path=$process.Path } catch { $path=$null }
        if ($path -and $path -ieq $launch.Executable) { throw 'This portable runtime is already running.' }
    }
    foreach ($entry in Get-ChildItem Env: | Where-Object Name -Like 'AZEROTH_LAB_*') { Remove-Item -LiteralPath ('Env:'+$entry.Name) }
    Start-Process -FilePath $launch.Executable -WorkingDirectory $launch.WorkingDirectory -ArgumentList $launch.Arguments | Out-Null
} catch { Write-Error $_; exit 1 }
