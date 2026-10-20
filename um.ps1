param([Parameter(ValueFromRemainingArguments=$true)][string[]]$UmArgs)
$env:UM_KB = Join-Path $PSScriptRoot 'universal-modder/knowledge'
$env:UM_HOME = Join-Path $PSScriptRoot 'um-data'
& (Join-Path $PSScriptRoot '.venv/Scripts/um.exe') @UmArgs
exit $LASTEXITCODE

