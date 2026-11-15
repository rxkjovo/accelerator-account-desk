param([string]$CompilerPath, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $repoDirectory 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj') -Raw)).Project.PropertyGroup.Version
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$publishDirectory = Join-Path $repoDirectory "dist\加速账户助手-$version"
$releaseDirectory = Join-Path $repoDirectory "dist\releases\v$version"
& (Join-Path $PSScriptRoot 'assert-clean-publish.ps1') -PublishDirectory $publishDirectory
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishDirectory 'AcceleratorAccountDesk.dll')).Version.ToString(3) -ne $version) { throw 'Published application version mismatch.' }
Copy-Item -LiteralPath (Join-Path $repoDirectory 'README.md') -Destination (Join-Path $publishDirectory '使用说明.md') -Force
if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
    $candidates = @(
        (Join-Path $repoDirectory '.tools\InnoSetup\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1)
    )
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath -PathType Leaf)) {
    throw 'Install Inno Setup from https://jrsoftware.org/isdl.php, or pass -CompilerPath to ISCC.exe.'
}
$arguments = @(
    "/DAppVersion=$version", "/DPublishDirectory=$publishDirectory",
    "/DRepositoryDirectory=$repoDirectory", "/DReleaseDirectory=$releaseDirectory",
    (Join-Path $repoDirectory 'installer\AcceleratorAccountDesk.iss')
)
& $CompilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Windows installer compilation failed.' }
$installer = Join-Path $releaseDirectory "AcceleratorAccountDesk-$version-Setup-win-x64.exe"
$item = Get-Item -LiteralPath $installer
$report = [ordered]@{ Version = $version; FileName = $item.Name; Size = $item.Length; Sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() }
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'installer-manifest.json') -Encoding utf8NoBOM
$report | ConvertTo-Json
