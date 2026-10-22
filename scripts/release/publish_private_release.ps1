#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][string]$Verification,
    [Parameter(Mandatory)][string]$Notes,
    [Parameter(Mandatory)][string]$Report,
    [string]$Tag='v0.5.0-private'
)
$ErrorActionPreference='Stop'
$repo='Solece/MegaAzeroth'
$api='https://api.github.com/repos/'+$repo
$env:GIT_TERMINAL_PROMPT='0'
$env:GCM_INTERACTIVE='never'
$credentialLines="protocol=https`nhost=github.com`npath=$repo.git`n`n" | git credential fill 2>$null
$credentialValues=@{}
foreach($line in $credentialLines){$parts=$line -split '=',2;if($parts.Length -eq 2){$credentialValues[$parts[0]]=$parts[1]}}
if(!$credentialValues['password']){throw 'GitHub credential unavailable'}
$headers=@{Authorization=('Bearer '+$credentialValues['password']);Accept='application/vnd.github+json';'User-Agent'='MegaAzeroth-ReleaseBuilder';'X-GitHub-Api-Version'='2026-03-10'}
try {
    $repository=Invoke-RestMethod -Uri $api -Headers $headers
    if(!$repository.private -or $repository.visibility -ne 'private'){throw 'Refusing to upload the personal game archive to a public repository.'}
    $verificationData=Get-Content -LiteralPath $Verification -Raw -Encoding UTF8 | ConvertFrom-Json
    if(!$verificationData.VerifiedAllArchiveMembers -or !$verificationData.NativeExtractedMenu.Passed){throw 'Archive and extracted-native acceptance must pass before upload.'}
    $actualHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $Archive).Hash.ToLowerInvariant()
    if($actualHash -ne $verificationData.Sha256){throw 'Archive differs from accepted verification report.'}
    $all=Invoke-RestMethod -Uri ($api+'/releases?per_page=100') -Headers $headers
    $matching=@($all | Where-Object tag_name -eq $Tag)
    if($matching.Count -gt 1){throw 'Multiple releases match tag.'}
    if($matching.Count -eq 1){
        $release=$matching[0]
        if(!$release.draft){throw 'An already published release exists; do not replace it automatically.'}
    } else {
        $body=@{tag_name=$Tag;target_commitish=$Commit;name=('MegaAzeroth '+$Tag+' - private portable build');body=[IO.File]::ReadAllText($Notes);draft=$true;prerelease=$true} | ConvertTo-Json
        $release=Invoke-RestMethod -Method Post -Uri ($api+'/releases') -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    }
    foreach($path in @($Archive,($Archive+'.sha256'),$Verification)){
        $name=Split-Path -Leaf $path
        $existing=@($release.assets | Where-Object name -eq $name)
        if($existing.Count -gt 0){
            $localHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
            if($existing.Count -ne 1 -or $existing[0].size -ne (Get-Item -LiteralPath $path).Length -or $existing[0].digest -ne ('sha256:'+$localHash)){throw ('Existing draft asset does not match: '+$name)}
            continue
        }
        $mime=if($name.EndsWith('.zip')){'application/zip'}elseif($name.EndsWith('.json')){'application/json'}else{'text/plain'}
        Write-Output ('Uploading '+$name+' ('+(Get-Item -LiteralPath $path).Length+' bytes)')
        $asset=Invoke-RestMethod -Method Post -Uri ('https://uploads.github.com/repos/'+$repo+'/releases/'+$release.id+'/assets?name='+[Uri]::EscapeDataString($name)) -Headers $headers -ContentType $mime -InFile $path
        $localHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
        if($asset.size -ne (Get-Item -LiteralPath $path).Length -or $asset.digest -ne ('sha256:'+$localHash)){throw ('Uploaded asset digest mismatch: '+$name)}
        Write-Output ('Verified server SHA256 for '+$name)
    }
    $repository=Invoke-RestMethod -Uri $api -Headers $headers
    if(!$repository.private){throw 'Repository visibility changed during upload; leave draft unpublished.'}
    $published=Invoke-RestMethod -Method Patch -Uri ($api+'/releases/'+$release.id) -Headers $headers -ContentType 'application/json' -Body '{"draft":false}'
    if($published.draft){throw 'Release remains a draft.'}
    $zipAsset=@($published.assets | Where-Object name -eq (Split-Path -Leaf $Archive))
    if($zipAsset.Count -ne 1){throw 'Published archive asset missing.'}
    $result=@{Repository=$repo;Private=$repository.private;ReleaseUrl=$published.html_url;Tag=$Tag;AssetUrl=$zipAsset[0].browser_download_url;AssetApiUrl=$zipAsset[0].url;AssetBytes=$zipAsset[0].size;AssetDigest=$zipAsset[0].digest;SourceCommit=$Commit;PublishedUtc=$published.published_at}
    [IO.File]::WriteAllText($Report,($result|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    $result|ConvertTo-Json
} finally {
    $credentialValues.Clear();$credentialLines=$null;$headers.Clear()
}
