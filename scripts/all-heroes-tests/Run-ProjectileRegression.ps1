param(
 [string]$LabRoot=(Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
 [string]$ReportFolder=''
)
$ErrorActionPreference='Stop'
$lab=[IO.Path]::GetFullPath($LabRoot)
if(!$ReportFolder){$ReportFolder=Join-Path $lab ('reports\projectile-regression-'+(Get-Date -Format yyyyMMdd-HHmmss))}
$report=[IO.Path]::GetFullPath($ReportFolder)
if(Test-Path -LiteralPath $report){throw 'Use a new report folder; existing evidence is never overwritten'}
New-Item -ItemType Directory -Path $report -Force|Out-Null
foreach($name in @('Program.cs','ManagedWorld.cs','ProjectileRegression.csproj')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('projectile-regression\'+$name)) -Destination (Join-Path $report $name)
}
$source=Join-Path $lab 'src\AzerothAbilities\Plugin.cs'
$text=[IO.File]::ReadAllText($source)
$start=$text.IndexOf('internal sealed class SpellProjectile')
$end=$text.IndexOf('public sealed class AbilityBehaviour',$start)
if($start -lt 0 -or $end -le $start){throw 'Actual production projectile class was not found'}
$subject="using UnityEngine;`nusing Assets.Scripts.Actors.Enemies;`nnamespace AzerothAbilities;`n"+$text.Substring($start,$end-$start)
[IO.File]::WriteAllText((Join-Path $report 'SpellProjectile.Subject.cs'),$subject)
Copy-Item -LiteralPath (Join-Path $lab 'src\AzerothAbilities\TargetAcquisition.cs') -Destination (Join-Path $report 'TargetAcquisition.cs')
@{productionSourceSha256=(Get-FileHash -LiteralPath $source).Hash;targetAcquisitionSha256=(Get-FileHash -LiteralPath (Join-Path $report 'TargetAcquisition.cs')).Hash;managedStandIns=$true;nativeRenderingVerified=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $report 'provenance.json') -Encoding UTF8
& dotnet run --project (Join-Path $report 'ProjectileRegression.csproj') -c Release -- (Join-Path $report 'result.json')
if($LASTEXITCODE -ne 0){throw 'Actual production projectile regression failed'}
