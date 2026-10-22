#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Resolve-LaunchPath([string]$Root,[string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative)) { throw 'Expected a relative bundle path.' }
    $rootPath=[IO.Path]::GetFullPath($Root).TrimEnd('\','/')
    $path=[IO.Path]::GetFullPath((Join-Path $rootPath $Relative))
    if (!$path.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw ('Path escapes bundle: '+$Relative) }
    return $path
}
function Assert-LaunchHash([string]$Path,[string]$Sha256) {
    if ($Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or !(Test-Path -LiteralPath $Path -PathType Leaf)) { throw ('Missing or unpinned required file: '+$Path) }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash -ine $Sha256) { throw ('Bundle hash mismatch: '+$Path) }
}
function Assert-NativeGame([string]$Runtime) {
    $guard=@{
        'Megabonk.exe'='E2FA5120FBF41837891E0CA372210865B0EDD3826ABDB0E36D263B0F40FD5F01'
        'GameAssembly.dll'='4350A7AE25BA7AEC35677C213FDCEABBB638676BB00AB59131D7D9C37B6E3D9E'
        'Megabonk_Data/il2cpp_data/Metadata/global-metadata.dat'='17B506A77616B943B414B9A42D35E3ACBDAF71EEC5DC6C470FC4DA338010D550'
    }
    foreach ($file in $guard.Keys) { Assert-LaunchHash (Resolve-LaunchPath $Runtime $file) $guard[$file] }
}
function Test-BundledRuntime([string]$PackageRoot,[switch]$VerifyAll) {
    $releasePath=Join-Path $PackageRoot 'release.json'
    if (!(Test-Path -LiteralPath $releasePath)) {
        foreach ($version in @('all-heroes-v6','all-heroes-v5')) {
            $existing=Resolve-LaunchPath $PackageRoot ('builds/'+$version+'/Megabonk-Lab')
            if (!(Test-Path -LiteralPath $existing)) { continue }
            Assert-NativeGame $existing
            return [pscustomobject]@{RuntimeRoot=$existing;ReleaseId=('existing-local-'+$version);VerifiedAll=$false;Source=('Local prepared '+$version+' fallback')}
        }
        throw 'Extract the complete portable archive first. A source-only clone does not include the prepared runtime.'
    }
    $release=Get-Content -LiteralPath $releasePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($release.Format -ne 'megaazeroth-bundled-release-v1' -or !$release.BundledRuntime) { throw 'This launcher expects a complete bundled portable release.' }
    $runtime=Resolve-LaunchPath $PackageRoot $release.RuntimePath
    $fileManifest=Resolve-LaunchPath $PackageRoot $release.RuntimeFiles.Path
    Assert-LaunchHash $fileManifest $release.RuntimeFiles.Sha256
    $manifest=Get-Content -LiteralPath $fileManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.Format -ne 'megaazeroth-bundled-files-v1' -or @($manifest.Files).Count -eq 0) { throw 'Invalid or empty runtime file manifest.' }
    Assert-NativeGame $runtime
    $state=Join-Path $PackageRoot '.bundle-verified.json'; $hasPrevious=$false
    if (Test-Path -LiteralPath $state) {
        try { $previous=Get-Content -LiteralPath $state -Raw -Encoding UTF8 | ConvertFrom-Json; $hasPrevious=$previous.ManifestSha256 -ieq $release.RuntimeFiles.Sha256 } catch { $hasPrevious=$false }
    }
    $full=$VerifyAll -or !$hasPrevious
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $checkedParents=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($manifest.Files)) {
        $relative=$file.Path.Replace('\','/')
        if (!$seen.Add($relative)) { throw ('Duplicate runtime manifest path: '+$relative) }
        $path=Resolve-LaunchPath $runtime $relative
        $mutable=$relative -match '^BepInEx/(interop/|cache/|config/|LogOutput\.log$)'
        if ($mutable -and $hasPrevious -and !$VerifyAll) { continue }
        if (!$full -and $relative -notmatch '^BepInEx/plugins/.+\.dll$') { continue }
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Bundle file missing: '+$relative) }
        $item=Get-Item -LiteralPath $path -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Bundle contains a reparse point: '+$relative) }
        $parent=Split-Path -Parent $path
        while ($parent -and $checkedParents.Add($parent)) {
            if (((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Bundle folder is a reparse point: '+$parent) }
            if ($parent -eq [IO.Path]::GetFullPath($runtime).TrimEnd('\','/')) { break }
            $parent=Split-Path -Parent $parent
        }
        if ($item.Length -ne [long]$file.Bytes) { throw ('Bundle file truncated: '+$relative) }
        # Full first verification checks all supplied files; subsequent native-generated
        # state is allowed to change. Core RVA inputs and plugin DLLs stay pinned.
        if ($full -or $relative -match '^BepInEx/plugins/.+\.dll$') { Assert-LaunchHash $path $file.Sha256 }
    }
    if ($full) {
        $tmp=$state+'.tmp-'+[Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllText($tmp,(@{ManifestSha256=$release.RuntimeFiles.Sha256;VerifiedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $state) { $backup=$state+'.old'; [IO.File]::Replace($tmp,$state,$backup); Remove-Item -LiteralPath $backup } else { [IO.File]::Move($tmp,$state) }
    }
    return [pscustomobject]@{RuntimeRoot=$runtime;ReleaseId=$release.ReleaseId;VerifiedAll=$full;Source='Bundled runtime; no game/source downloads or importer required'}
}
function Get-BundledLaunch([string]$PackageRoot,[string]$RuntimeRoot,[ValidateSet('Test','Normal')][string]$Mode='Test') {
    $reports=Join-Path $PackageRoot 'reports'; [IO.Directory]::CreateDirectory($reports) | Out-Null
    $log=Join-Path $reports ('play-'+$Mode.ToLowerInvariant()+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+$PID+'.log')
    $arguments=@('-azeroth-elwynn','-screen-fullscreen','0','-screen-width','1600','-screen-height','900')
    if ($Mode -eq 'Test') { $arguments+=@('-azeroth-test-menu','-azeroth-decor-density=1.2','-azeroth-talents-max','-azeroth-talent-points=999') }
    $arguments+=@('-logFile',('"'+$log+'"'))
    return [pscustomobject]@{Executable=(Join-Path $RuntimeRoot 'Megabonk.exe');WorkingDirectory=$RuntimeRoot;Arguments=$arguments;Mode=$Mode;LogFile=$log}
}
Export-ModuleMember -Function Test-BundledRuntime,Get-BundledLaunch
