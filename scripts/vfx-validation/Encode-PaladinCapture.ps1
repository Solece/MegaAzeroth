param(
    [Parameter(Mandatory=$true)][string]$CaptureFolder,
    [string]$Ffmpeg='ffmpeg'
)
$resolvedCapture=(Resolve-Path -LiteralPath $CaptureFolder).Path
$recording=Get-Content -LiteralPath (Join-Path $resolvedCapture 'recording.json') -Raw | ConvertFrom-Json
$rows=@($recording.frames | Sort-Object { [int]$_.frame })
if($rows.Count -lt 2){throw 'At least two measured native frames are required.'}
$concat=New-Object System.Collections.Generic.List[string]
$concat.Add('ffconcat version 1.0')
$lastFile=$null
for($index=0;$index -lt $rows.Count;$index++){
    $row=$rows[$index]
    $framePath=Join-Path $resolvedCapture ('frames/{0:D4}.ppm' -f [int]$row.frame)
    if(-not(Test-Path -LiteralPath $framePath)){throw "Missing measured frame: $framePath"}
    $escaped=$framePath.Replace('\','/').Replace("'","'\''")
    $concat.Add("file '$escaped'")
    if($index+1 -lt $rows.Count){$duration=[double]$rows[$index+1].wallSeconds-[double]$row.wallSeconds}
    elseif($null -ne $recording.recordingEndedWallSeconds){$duration=[double]$recording.recordingEndedWallSeconds-[double]$row.wallSeconds}
    else{$duration=[double]$row.wallSeconds-[double]$rows[$index-1].wallSeconds}
    if($duration -le 0){throw "Non-monotonic or incomplete timestamp at frame $($row.frame)"}
    $concat.Add('duration '+$duration.ToString('0.000000000',[Globalization.CultureInfo]::InvariantCulture))
    $lastFile="file '$escaped'"
}
# The repeated terminal image expresses the measured final display duration;
# it is not an interpolated or invented gameplay frame.
$concat.Add($lastFile)
$concatPath=Join-Path $resolvedCapture 'native-timestamps.ffconcat'
[IO.File]::WriteAllLines($concatPath,$concat,[Text.UTF8Encoding]::new($false))
$outputVideo=Join-Path $resolvedCapture 'consecration-full-cycle.mp4'
& $Ffmpeg -nostdin -hide_banner -loglevel warning -y -safe 0 -f concat -i $concatPath -fps_mode vfr -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -movflags +faststart $outputVideo
if($LASTEXITCODE -ne 0){throw "Video encoding failed: $LASTEXITCODE"}
Write-Output $outputVideo
Write-Output 'VFR uses measured timestamps; cadence gaps remain visible. No fixed15FPS timing or interpolation is fabricated. This video does not establish original-source A/B fidelity.'
