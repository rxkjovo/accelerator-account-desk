param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $repoDirectory 'src\AcceleratorDesk.App\AcceleratorDesk.App.csproj') -Raw)).Project.PropertyGroup.Version
$publishDirectory = Join-Path $repoDirectory "dist\加速账户助手-$version"
$releaseDirectory = Join-Path $repoDirectory "dist\releases\v$version"
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'AcceleratorDesk.Updater.exe'))) { throw 'Build the complete application first.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $publishDirectory 'AcceleratorAccountDesk.dll')).Version.ToString(3) -ne $version) { throw 'Published application version mismatch.' }
Copy-Item -LiteralPath (Join-Path $repoDirectory 'docs\使用说明.txt') -Destination (Join-Path $publishDirectory '先读我-使用说明.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoDirectory 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE') -Force
& (Join-Path $PSScriptRoot 'assert-clean-publish.ps1') -PublishDirectory $publishDirectory
$channel = Get-Content -LiteralPath (Join-Path $publishDirectory 'update-channel.json') -Raw | ConvertFrom-Json
if ($channel.Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$') { throw 'Invalid update repository.' }
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
$archiveName = "AcceleratorAccountDesk-$version-win-x64.zip"
$archivePath = Join-Path $releaseDirectory $archiveName
$temporary = Join-Path $releaseDirectory ("package-" + [Guid]::NewGuid().ToString('N') + '.partial')
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $temporary, [IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath $temporary -Destination $archivePath -Force
}
finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
$archive = Get-Item -LiteralPath $archivePath
$manifest = [ordered]@{
    FormatVersion = 1
    ProductId = 'AcceleratorAccountDesk'
    Version = $version
    RuntimeId = 'win-x64'
    ArchiveName = $archiveName
    ArchiveSize = $archive.Length
    Sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseDirectory 'update-manifest.json') -Encoding utf8NoBOM
$notes = @"
加速账户助手 v$version · Windows 10/11 x64

- 新增 GitHub Releases 在线更新：检查新版、下载进度、取消、完整性校验和更新后重启。
- 改进 GitHub 匿名接口限流时的更新检查，自动通过同仓库的官方 Release 清单继续检查。
- 新版本安装到独立文件夹，继续使用本机账号、密码、手机号及兑换记录，旧版保留供回退。
- 提供完整分享包和使用说明，内含 .NET 运行时，无需安装开发工具。
- 修复个人兑换码文件无法读取时整张列表空白的问题；内置的 7 个码继续显示和复制，原文件保留。
- 保留 QQ 注册结果保存、密码查看及兑换码复制功能。

推荐下载 AcceleratorAccountDesk-$version-Setup-win-x64.exe，双击进入中文安装向导，可自定义目录并创建快捷方式。
免安装版为 $archiveName，全部解压后双击「加速账户助手.exe」，请保留整个文件夹。
软件更新入口：左下角「检查更新」，或「设置与说明 → 软件更新」。
QQ 官方内嵌页需要 Microsoft Edge WebView2 Runtime；验证码、授权及额外安全验证由本人完成。

账号和密码保存在使用者自己的电脑上，不随安装包分发。密码绑定原 Windows 用户。
源码、构建说明和贡献入口：https://github.com/$($channel.Repository)
"@
$notes | Set-Content -LiteralPath (Join-Path $releaseDirectory 'release-notes.md') -Encoding utf8NoBOM
$manifest | ConvertTo-Json
Write-Output $archivePath
