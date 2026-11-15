param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
function Get-SharedFileHash([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
    finally { $stream.Dispose() }
}
$version = ([xml](Get-Content -LiteralPath (Join-Path $repoDirectory 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj') -Raw)).Project.PropertyGroup.Version
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{69BFC873-8DCB-4A68-9D7F-FB7E67A70EF9}_is1'
if (Test-Path -LiteralPath $registryPath) { throw 'A user installation is already registered. Run this installer check on a separate Windows user.' }
$fixtureRoot = Join-Path $repoDirectory ('artifacts\installer-smoke-' + [Guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $fixtureRoot '自选安装目录 中文 空格'
$dataDirectory = Join-Path $fixtureRoot '保留账号数据'
New-Item -ItemType Directory -Path $installDirectory,$dataDirectory -Force | Out-Null
$recordSentinel = Join-Path $dataDirectory 'accounts.db'
[IO.File]::WriteAllText($recordSentinel, 'isolated user record sentinel; not a real database')
$recordHash = (Get-FileHash -LiteralPath $recordSentinel -Algorithm SHA256).Hash
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '加速账户助手.lnk'
$shortcutHash = if (Test-Path -LiteralPath $desktopShortcut) { (Get-FileHash -LiteralPath $desktopShortcut -Algorithm SHA256).Hash } else { $null }
$defaultData = Join-Path $env:LOCALAPPDATA 'AcceleratorAccountDesk\accounts.db'
$defaultDataHash = if (Test-Path -LiteralPath $defaultData) { Get-SharedFileHash $defaultData } else { $null }
$log = Join-Path $fixtureRoot 'install.log'
$arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=',('/DIR="' + $installDirectory + '"'),('/LOG="' + $log + '"'))
$process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(45000)) { throw 'Fixture installer timed out.' }
if ($process.ExitCode -ne 0) { throw ('Fixture installer failed: ' + $process.ExitCode) }
$installedProgram = Join-Path $installDirectory "加速账户助手-$version"
$executable = Join-Path $installedProgram '加速账户助手.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Installer did not honor the custom Unicode path.' }
$installedVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $installedProgram 'AcceleratorAccountDesk.dll')).Version.ToString(3)
if ($installedVersion -ne $version) { throw 'Installed version mismatch.' }
$registry = Get-ItemProperty -LiteralPath $registryPath
if ([IO.Path]::TrimEndingDirectorySeparator($registry.InstallLocation) -ne $installDirectory) { throw 'Uninstall registration did not honor the selected path.' }
& (Join-Path $PSScriptRoot 'assert-clean-publish.ps1') -PublishDirectory $installedProgram
$launchData = Join-Path $dataDirectory '首次启动记录'
$launchInfo = [Diagnostics.ProcessStartInfo]::new($executable)
$launchInfo.UseShellExecute = $false
$launchInfo.WorkingDirectory = $installedProgram
$launchInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Minimized
$launchInfo.ArgumentList.Add('--data-dir')
$launchInfo.ArgumentList.Add($launchData)
$applicationProcess = [Diagnostics.Process]::Start($launchInfo)
try {
    $startupWait = [Diagnostics.Stopwatch]::StartNew()
    while (-not $applicationProcess.HasExited -and $applicationProcess.MainWindowHandle -eq [IntPtr]::Zero -and $startupWait.Elapsed.TotalSeconds -lt 15) {
        Start-Sleep -Milliseconds 100
        $applicationProcess.Refresh()
    }
    if ($applicationProcess.HasExited -or $applicationProcess.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The installed application did not open its main window.' }
    if (-not (Test-Path -LiteralPath (Join-Path $launchData 'accounts.db'))) { throw 'First launch did not create isolated user records.' }
}
finally {
    if (-not $applicationProcess.HasExited) {
        $applicationProcess.CloseMainWindow() | Out-Null
        if (-not $applicationProcess.WaitForExit(10000)) { throw 'The isolated installed application did not exit normally.' }
    }
    $applicationProcess.Dispose()
}
$firstLaunchHash = Get-SharedFileHash (Join-Path $launchData 'accounts.db')
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$resolvedUninstaller = (Resolve-Path -LiteralPath $uninstaller).Path
if (-not $resolvedUninstaller.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstall target escaped the isolated fixture.' }
$uninstallArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $fixtureRoot 'uninstall.log') + '"'))
$uninstall = Start-Process -FilePath $resolvedUninstaller -ArgumentList $uninstallArguments -PassThru -WindowStyle Hidden
if (-not $uninstall.WaitForExit(45000)) { throw 'Fixture uninstaller timed out.' }
if ($uninstall.ExitCode -ne 0) { throw ('Fixture uninstall failed: ' + $uninstall.ExitCode) }
if (Test-Path -LiteralPath $executable) { throw 'Installed program was not removed by uninstall.' }
if (Test-Path -LiteralPath $registryPath) { throw 'Fixture uninstall registration was not removed.' }
if ((Get-FileHash -LiteralPath $recordSentinel -Algorithm SHA256).Hash -ne $recordHash) { throw 'Uninstall changed the user record sentinel.' }
if ((Get-SharedFileHash (Join-Path $launchData 'accounts.db')) -ne $firstLaunchHash) { throw 'Uninstall changed first-launch account records.' }
if ($shortcutHash -and (Get-FileHash -LiteralPath $desktopShortcut -Algorithm SHA256).Hash -ne $shortcutHash) { throw 'Isolated installer changed the existing user desktop shortcut.' }
if ($defaultDataHash -and (Get-SharedFileHash $defaultData) -ne $defaultDataHash) { throw 'The current user account database changed during the installer check.' }
$report = [ordered]@{ Installer = [IO.Path]::GetFileName($InstallerPath); Version = $version; CustomUnicodeAndSpaceDirectory = $true; ProgramFilesInstalled = $true; RealInstalledApplicationOpened = $true; UninstallRegistrationVerified = $true; UninstallCompleted = $true; UserDataPreserved = $true; ExistingShortcutPreserved = $true; FixtureDirectory = $fixtureRoot }
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixtureRoot 'report.json') -Encoding utf8NoBOM
$report | ConvertTo-Json
