using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcceleratorDesk.Update;

public static class UpdatePackage
{
    public static string Install(string archivePath, UpdateManifest manifest, string currentDirectory, string repository)
    {
        manifest.Validate(); new UpdateChannel(repository).Validate();
        currentDirectory = Path.GetFullPath(currentDirectory);
        var parent = Directory.GetParent(currentDirectory)?.FullName ?? throw new InvalidDataException("当前程序目录不支持在线更新。");
        if (!Directory.Exists(currentDirectory)) throw new InvalidDataException("原程序目录不存在，未安装更新。");
        var currentAssembly = Path.Combine(currentDirectory, "AcceleratorAccountDesk.dll");
        if (!File.Exists(currentAssembly)) throw new InvalidDataException("原目录缺少程序信息，未安装更新。");
        if (AssemblyName.GetAssemblyName(currentAssembly).Version is not { } current || UpdateVersion.Parse(manifest.Version) <= current)
            throw new InvalidDataException("更新版本必须高于当前版本。");
        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length != manifest.ArchiveSize) throw new InvalidDataException("下载包大小不一致，未安装更新。");
        using (var archive = File.OpenRead(archivePath))
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(archive), Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("下载包校验失败，未安装更新。");
        var target = Path.Combine(parent, "加速账户助手-" + manifest.Version);
        if (Directory.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("目标版本目录是链接，未安装更新。");
            var markerPath = Path.Combine(target, "installed-update.json");
            if (File.Exists(markerPath))
            {
                try
                {
                    var previous = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(markerPath));
                    if (previous?.Version == manifest.Version && previous.Sha256 == manifest.Sha256) { ValidateProgram(target, manifest); return target; }
                }
                catch (Exception error) when (error is JsonException or BadImageFormatException) { }
            }
            throw new InvalidDataException("目标版本目录已经存在，已保留其中的文件。请移开该目录后重试。");
        }
        var staging = Path.Combine(parent, ".accelerator-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(archivePath); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long expanded = 0;
            if (archive.Entries.Count > 5000) throw new InvalidDataException("更新包文件数量异常。");
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!SafeEntry(name) || !paths.Add(name.TrimEnd('/')) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                    || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("更新包包含不安全或重复的路径，未安装。");
                expanded = checked(expanded + entry.Length); if (expanded > 805306368) throw new InvalidDataException("更新包解压体积异常。");
                var destination = Path.GetFullPath(Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包路径超出安装目录。");
                if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open(); using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[131072]; long copied = 0; int count;
                while ((count = source.Read(buffer)) > 0)
                { copied += count; if (copied > entry.Length) throw new InvalidDataException("更新包解压长度异常。"); output.Write(buffer, 0, count); }
                if (copied != entry.Length) throw new InvalidDataException("更新包文件长度不完整。");
            }
            ValidateProgram(staging, manifest);
            File.WriteAllText(Path.Combine(staging, "update-channel.json"), JsonSerializer.Serialize(new UpdateChannel(repository)));
            File.WriteAllText(Path.Combine(staging, "installed-update.json"), JsonSerializer.Serialize(manifest));
            Directory.Move(staging, target); return target;
        }
        finally
        {
            if (Directory.Exists(staging) && Path.GetDirectoryName(staging) == parent && Path.GetFileName(staging).StartsWith(".accelerator-update-", StringComparison.Ordinal))
                Directory.Delete(staging, true);
        }
    }

    private static bool SafeEntry(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('\\') || name.StartsWith('/') || name.Contains(':')) return false;
        foreach (var segment in name.TrimEnd('/').Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
            var lower = segment.ToLowerInvariant();
            if (lower is "settings.json" or "redemption-codes.json" or "qq-registration-draft.bin" or "qq-browser"
                || lower.EndsWith(".db") || lower.EndsWith(".db-wal") || lower.EndsWith(".db-shm") || lower.EndsWith(".lock")) return false;
        }
        return true;
    }

    private static void ValidateProgram(string directory, UpdateManifest manifest)
    {
        foreach (var file in new[] { "加速账户助手.exe", "AcceleratorAccountDesk.dll", "AcceleratorAccountDesk.runtimeconfig.json", "AcceleratorDesk.Updater.exe", "AcceleratorDesk.Updater.dll", "AcceleratorDesk.Updater.deps.json", "AcceleratorDesk.Updater.runtimeconfig.json", "AcceleratorDesk.Update.dll", "coreclr.dll", "e_sqlite3.dll", "WebView2Loader.dll" })
            if (!File.Exists(Path.Combine(directory, file)) || new FileInfo(Path.Combine(directory, file)).Length == 0) throw new InvalidDataException("更新包缺少必要的程序文件。");
        try
        {
            var assembly = AssemblyName.GetAssemblyName(Path.Combine(directory, "AcceleratorAccountDesk.dll"));
            if (assembly.Name != "AcceleratorAccountDesk" || assembly.Version != UpdateVersion.Parse(manifest.Version)) throw new InvalidDataException("更新包内的程序版本与清单不一致。");
        }
        catch (BadImageFormatException) { throw new InvalidDataException("更新包中的程序文件无效。"); }
    }
}
