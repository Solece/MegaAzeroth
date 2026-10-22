#Requires -Version 5.1
[CmdletBinding()]
param([ValidateSet('Test','Normal','Choose')][string]$Mode='Choose',[Alias('Verify')][switch]$VerifyOnly,[switch]$VerifyAll,[switch]$CollectLogs)
$ErrorActionPreference='Stop'
try {
    if($CollectLogs){& (Join-Path $PSScriptRoot 'Collect-Logs.ps1') -PackageRoot $PSScriptRoot;exit 0}
    if($Mode -eq 'Choose' -and !$VerifyOnly){
        Write-Host '1: Test (default): 999 points, maximum talents; progression writes suppressed.'
        Write-Host '2: Normal progression: native game profile and normal saves.'
        Write-Host '3: Collect diagnostic ZIP (works even if game verification fails).'
        $answer=Read-Host 'Choose 1/2/3; Enter starts Test'
        if($answer -eq '3'){
            & (Join-Path $PSScriptRoot 'Collect-Logs.ps1') -PackageRoot $PSScriptRoot
            $null=Read-Host 'Diagnostic ZIP is in reports. Press Enter to close'
            exit 0
        }
        $Mode=if($answer -eq '' -or $answer -eq '1'){'Test'}elseif($answer -eq '2'){'Normal'}else{throw 'Expected 1, 2, 3, or Enter.'}
    }
    Import-Module (Join-Path $PSScriptRoot 'Launcher.Core.psm1') -Force
    $install=Test-BundledRuntime -PackageRoot $PSScriptRoot -VerifyAll:($VerifyAll -or $VerifyOnly)
    if($VerifyOnly){$install|ConvertTo-Json;exit 0}
    $launch=Get-BundledLaunch -PackageRoot $PSScriptRoot -RuntimeRoot $install.RuntimeRoot -Mode $Mode
    foreach($process in @(Get-Process -Name Megabonk -ErrorAction SilentlyContinue)){
        try{$path=$process.Path}catch{$path=$null}
        if($path -and $path -ieq $launch.Executable){throw 'This portable runtime is already running.'}
    }
    foreach($entry in Get-ChildItem Env:|Where-Object Name -Like 'AZEROTH_LAB_*'){Remove-Item -LiteralPath ('Env:'+$entry.Name)}
    Start-Process -FilePath $launch.Executable -WorkingDirectory $launch.WorkingDirectory -ArgumentList $launch.Arguments|Out-Null
}catch{
    $failure=$_
    try{
        $reports=Join-Path $PSScriptRoot 'reports';[IO.Directory]::CreateDirectory($reports)|Out-Null
        $errorLog=Join-Path $reports ('launcher-error-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+$PID+'.txt')
        [IO.File]::WriteAllText($errorLog,$failure.ToString()+"`r`n"+$failure.ScriptStackTrace,[Text.UTF8Encoding]::new($false))
        Write-Host ('Launcher error saved: '+$errorLog)
        Write-Host 'Restart the launcher and choose 3 to collect the diagnostic ZIP.'
    }catch{}
    Write-Error -ErrorRecord $failure -ErrorAction Continue
    exit 1
}
