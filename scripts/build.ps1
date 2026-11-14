param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $repoDirectory '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$outputDirectory = Join-Path $repoDirectory 'dist\加速账户助手-0.2.0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoDirectory
try {
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'test.ps1') }
    & $sdkCommand publish 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj' -c Release -r win-x64 --self-contained true -o $outputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $originalExecutable = Join-Path $outputDirectory 'AcceleratorAccountDesk.exe'
    $friendlyExecutable = Join-Path $outputDirectory '加速账户助手.exe'
    Copy-Item -LiteralPath $originalExecutable -Destination $friendlyExecutable -Force
    Copy-Item -LiteralPath (Join-Path $repoDirectory 'README.md') -Destination (Join-Path $outputDirectory '使用说明.md') -Force
    Get-Item -LiteralPath $friendlyExecutable | Select-Object FullName, Length
    Get-FileHash -LiteralPath $friendlyExecutable -Algorithm SHA256
}
finally { Pop-Location }
