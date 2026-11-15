param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory '加速账户助手.exe') -PathType Leaf)) { throw 'The complete application publish folder is required.' }
$privateFiles = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force | Where-Object {
    $_.Name -match '^(settings\.json|redemption-codes\.json|qq-registration-draft\.bin|qq-browser|updates|backups?|\.git)$' -or
    $_.Name -match '\.(db|db-wal|db-shm|lock)$'
})
if ($privateFiles.Count -gt 0) { throw 'Publish folder contains local user data or a source checkout. Packaging stopped.' }
