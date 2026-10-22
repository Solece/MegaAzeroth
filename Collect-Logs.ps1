#Requires -Version 5.1
[CmdletBinding()]
param([string]$PackageRoot=$PSScriptRoot)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
$root=[IO.Path]::GetFullPath($PackageRoot).TrimEnd('\','/')
$reports=Join-Path $root 'reports'
[IO.Directory]::CreateDirectory($reports)|Out-Null
$notes=New-Object 'System.Collections.Generic.List[string]'
function Read-DiagnosticText([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if(!$full.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Diagnostic path outside package'}
    $cursor=$full
    while($cursor -and $cursor -ne $root){
        if(((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Diagnostic link skipped'}
        $cursor=Split-Path -Parent $cursor
    }
    $stream=[IO.File]::Open($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        $limit=2MB
        if($stream.Length -gt $limit){$null=$stream.Seek(-$limit,[IO.SeekOrigin]::End);$notes.Add('Only final 2 MiB included: '+[IO.Path]::GetFileName($full))}
        $buffer=New-Object byte[] ([int][Math]::Min($limit,$stream.Length))
        $count=0
        while($count -lt $buffer.Length){$read=$stream.Read($buffer,$count,$buffer.Length-$count);if($read -eq 0){break};$count+=$read}
        $text=[Text.Encoding]::UTF8.GetString($buffer,0,$count)
    } finally {$stream.Dispose()}
    foreach($privatePath in @(@{Path=$root;Label='%PACKAGE_ROOT%'},@{Path=$env:USERPROFILE;Label='%USERPROFILE%'})){
        if(!$privatePath.Path){continue}
        foreach($variant in @($privatePath.Path,$privatePath.Path.Replace('\','/'))){
            $text=[Text.RegularExpressions.Regex]::Replace($text,[Text.RegularExpressions.Regex]::Escape($variant),$privatePath.Label,[Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }
    return $text
}
function Add-DiagnosticText($Archive,[string]$Name,[string]$Text) {
    $entry=$Archive.CreateEntry($Name,[IO.Compression.CompressionLevel]::Optimal)
    $writer=New-Object IO.StreamWriter ($entry.Open()),([Text.UTF8Encoding]::new($false))
    try{$writer.Write($Text)}finally{$writer.Dispose()}
}
$system=[ordered]@{CollectedUtc=[DateTime]::UtcNow.ToString('o');WindowsVersion=[Environment]::OSVersion.VersionString;OS64Bit=[Environment]::Is64BitOperatingSystem;Process64Bit=[Environment]::Is64BitProcess;PowerShell=$PSVersionTable.PSVersion.ToString()}
try{$system.Windows=@(Get-CimInstance Win32_OperatingSystem -ErrorAction Stop|Select-Object Caption,Version,OSArchitecture)}catch{$notes.Add('Windows detail unavailable')}
try{$system.CPU=@(Get-CimInstance Win32_Processor -ErrorAction Stop|Select-Object Name)}catch{$notes.Add('CPU detail unavailable')}
try{$system.GPU=@(Get-CimInstance Win32_VideoController -ErrorAction Stop|Select-Object Name,DriverVersion)}catch{$notes.Add('GPU detail unavailable')}
$destination=Join-Path $reports ('MegaAzeroth-diagnostics-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.zip')
$zipStream=[IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
$zip=[IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create)
try {
    Add-DiagnosticText $zip 'system.json' ($system|ConvertTo-Json -Depth 5)
    $selected=New-Object 'System.Collections.Generic.List[string]'
    foreach($name in @('release.json','build-info.json')){if(Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf){$selected.Add((Join-Path $root $name))}}
    foreach($pattern in @('play-*.log','launcher-error-*.txt')){
        foreach($file in @(Get-ChildItem -LiteralPath $reports -Filter $pattern -File|Sort-Object LastWriteTime -Descending|Select-Object -First 3)){$selected.Add($file.FullName)}
    }
    foreach($relative in @('Runtime','builds/all-heroes-v6/Megabonk-Lab','builds/all-heroes-v5/Megabonk-Lab')){
        $runtime=Join-Path $root $relative
        if(!(Test-Path -LiteralPath $runtime -PathType Container)){continue}
        $log=Join-Path $runtime 'BepInEx/LogOutput.log'
        if(Test-Path -LiteralPath $log -PathType Leaf){$selected.Add($log)}
        break
    }
    foreach($file in $selected){
        try{$name=$file.Substring($root.Length+1).Replace('\','/');$text=Read-DiagnosticText $file;Add-DiagnosticText $zip $name $text}
        catch{$notes.Add('Could not include '+[IO.Path]::GetFileName($file)+': '+$_.Exception.Message)}
    }
    Add-DiagnosticText $zip 'collection-notes.txt' (($notes -join "`r`n")+"`r`nOnly build metadata, recent launcher/game logs and basic system details are included. Saves, accounts and environment-variable dumps are not collected.")
}finally{$zip.Dispose();$zipStream.Dispose()}
Write-Output $destination
