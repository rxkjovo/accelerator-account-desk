param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $repoDirectory '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$projectFile = Join-Path $repoDirectory 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj'
$version = ([xml](Get-Content -LiteralPath $projectFile -Raw)).Project.PropertyGroup.Version
$outputDirectory = Join-Path $repoDirectory "dist\加速账户助手-$version"
$helperDirectory = Join-Path $repoDirectory ".tools\updater-publish-$version"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoDirectory
try {
    if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'test.ps1') }
    & $sdkCommand publish 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj' -c Release -r win-x64 --self-contained true -o $outputDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    & $sdkCommand publish 'src\AcceleratorDesk.Updater\AcceleratorDesk.Updater.csproj' -c Release -r win-x64 --self-contained true -o $helperDirectory --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
    foreach ($helperFile in @('AcceleratorDesk.Updater.exe', 'AcceleratorDesk.Updater.dll', 'AcceleratorDesk.Updater.deps.json', 'AcceleratorDesk.Updater.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $helperDirectory $helperFile) -Destination (Join-Path $outputDirectory $helperFile) -Force
    }
    $originalExecutable = Join-Path $outputDirectory 'AcceleratorAccountDesk.exe'
    $friendlyExecutable = Join-Path $outputDirectory '加速账户助手.exe'
    Copy-Item -LiteralPath $originalExecutable -Destination $friendlyExecutable -Force
    Copy-Item -LiteralPath (Join-Path $repoDirectory 'README.md') -Destination (Join-Path $outputDirectory '使用说明.md') -Force
    Get-Item -LiteralPath $friendlyExecutable | Select-Object FullName, Length
    Get-FileHash -LiteralPath $friendlyExecutable -Algorithm SHA256
}
finally { Pop-Location }
