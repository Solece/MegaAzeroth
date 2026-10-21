param(
    [string]$LabRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$ReportFolder=''
)
$ErrorActionPreference='Stop'
$lab=[IO.Path]::GetFullPath($LabRoot)
if(!$ReportFolder){$ReportFolder=Join-Path $lab ('reports\particle-regression-'+(Get-Date -Format yyyyMMdd-HHmmss-fff))}
$report=[IO.Path]::GetFullPath($ReportFolder)
if(Test-Path -LiteralPath $report){throw 'Use a new report folder; existing evidence is never overwritten'}
$source=Join-Path $lab 'src\AzerothAbilities\SpellVfx.cs'
if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw ('Production SpellVfx.cs not found: '+$source)}
$dotnet=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$sdks=@(& $dotnet --list-sdks)
if($LASTEXITCODE -ne 0 -or !($sdks -match '^10\.')){throw '.NET10 SDK/reference pack must already be installed; this runner does not install tools'}
New-Item -ItemType Directory -Path $report|Out-Null
$utf8=New-Object Text.UTF8Encoding($false)
$provenance=[ordered]@{
    Kind='Actual production particle helper regression';StartedUtc=[DateTime]::UtcNow.ToString('o')
    ProductionSource=$source;ProductionSourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    Dotnet=$dotnet;InstalledSdks=$sdks;UseAppHost=$false
    NoGameLaunch=$true;NoNativeAssets=$true;NativeRenderingVerified=$false;VisualEquivalencePassed=$false
    ReportFolder=$report;Passed=$false;Failure=$null
}
try {
    $text=[IO.File]::ReadAllText($source)
    $begin='// SOURCE_PARTICLE_MATH_BEGIN:'
    $end='// SOURCE_PARTICLE_MATH_END'
    if(([regex]::Matches($text,[regex]::Escape($begin))).Count -ne 1 -or ([regex]::Matches($text,[regex]::Escape($end))).Count -ne 1){throw 'Expected exactly one production particle helper BEGIN/END marker'}
    $start=$text.IndexOf("`n",$text.IndexOf($begin))+1
    $finish=$text.IndexOf($end,$start)
    if($start -le 0 -or $finish -le $start){throw 'Actual production particle helper block is invalid'}
    $subject="using System;`nnamespace AzerothAbilities;`n"+$text.Substring($start,$finish-$start)
    $subjectPath=Join-Path $report 'ActualParticleMath.cs'
    [IO.File]::WriteAllText($subjectPath,$subject,$utf8)
    foreach($name in @('Program.cs','ParticleRegression.csproj')){
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('particle-regression\'+$name)) -Destination (Join-Path $report $name)
    }
    [IO.File]::WriteAllText((Join-Path $report 'NuGet.Config'),'<configuration><packageSources><clear /></packageSources></configuration>',$utf8)
    $provenance['ExtractedHelperSha256']=(Get-FileHash -LiteralPath $subjectPath -Algorithm SHA256).Hash
    $provenance['ChecksProgramSha256']=(Get-FileHash -LiteralPath (Join-Path $report 'Program.cs') -Algorithm SHA256).Hash
    $provenance['ProjectSha256']=(Get-FileHash -LiteralPath (Join-Path $report 'ParticleRegression.csproj') -Algorithm SHA256).Hash
    $project=Join-Path $report 'ParticleRegression.csproj'
    & $dotnet restore $project --configfile (Join-Path $report 'NuGet.Config') --packages (Join-Path $report 'packages') --nologo 2>&1|Tee-Object -FilePath (Join-Path $report 'restore.log')
    if($LASTEXITCODE -ne 0){throw 'Offline particle regression restore failed'}
    & $dotnet build $project -c Release --no-restore --nologo -p:UseAppHost=false -p:UseSharedCompilation=false 2>&1|Tee-Object -FilePath (Join-Path $report 'build.log')
    if($LASTEXITCODE -ne 0){throw 'Particle regression build failed'}
    $dll=Join-Path $report 'bin\Release\net10.0\ParticleRegression.dll'
    $apphost=Join-Path $report 'bin\Release\net10.0\ParticleRegression.exe'
    if(Test-Path -LiteralPath $apphost){throw 'Unexpected apphost executable; refusing to run it'}
    $provenance['ManagedDllSha256']=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $provenance['RunCommand']=@($dotnet,$dll,(Join-Path $report 'result.json'))
    & $dotnet $dll (Join-Path $report 'result.json') 2>&1|Tee-Object -FilePath (Join-Path $report 'run.log')
    $provenance['ManagedExitCode']=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw 'Actual production particle helper regression failed'}
    $result=Get-Content -LiteralPath (Join-Path $report 'result.json') -Raw|ConvertFrom-Json
    if(!$result.Passed -or $result.Checks -ne 10390){throw 'Particle regression did not return the expected10390 successful assertions'}
    $provenance['Passed']=$true
    $provenance['Checks']=$result.Checks
}
catch {
    $provenance['Failure']=$_.Exception.Message
    throw
}
finally {
    $provenance['FinishedUtc']=[DateTime]::UtcNow.ToString('o')
    [IO.File]::WriteAllText((Join-Path $report 'provenance.json'),($provenance|ConvertTo-Json -Depth 8),$utf8)
}
Write-Output ('Particle regression PASS:10390 assertions. Report: '+$report)
