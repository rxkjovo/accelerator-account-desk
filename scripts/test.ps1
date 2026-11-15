param()
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $repoDirectory '.tools\dotnet\dotnet.exe'
$sdkCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoDirectory
try {
    foreach ($testProject in @('tests\AcceleratorDesk.Tests', 'tests\AcceleratorDesk.AppLogic.Tests', 'tests\AcceleratorDesk.Credentials.Tests', 'tests\AcceleratorDesk.Update.Tests', 'tests\AcceleratorDesk.Registration.Tests')) {
        & $sdkCommand run --project $testProject -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $testProject" }
    }
    & $sdkCommand run --project 'tests\AcceleratorDesk.VisualPreview' -c Release -- (Join-Path $repoDirectory 'artifacts\visual-preview')
    if ($LASTEXITCODE -ne 0) { throw 'Native form / visual checks failed.' }
}
finally { Pop-Location }
