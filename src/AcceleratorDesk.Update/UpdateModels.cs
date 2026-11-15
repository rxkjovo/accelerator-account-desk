using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcceleratorDesk.Update;

public sealed record UpdateChannel(string Repository)
{
    public static UpdateChannel Load(string applicationDirectory)
    {
        var path = Path.Combine(applicationDirectory, "update-channel.json");
        if (!File.Exists(path)) throw new InvalidDataException("尚未配置 GitHub 更新仓库，请联系软件发布者。");
        try
        {
            var channel = JsonSerializer.Deserialize<UpdateChannel>(File.ReadAllText(path)) ?? throw new InvalidDataException("更新渠道配置为空。");
            channel.Validate(); return channel;
        }
        catch (JsonException) { throw new InvalidDataException("更新渠道配置无法读取，请联系软件发布者。"); }
    }
    public void Validate()
    {
        if (!Regex.IsMatch(Repository ?? "", @"^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("GitHub 更新仓库格式应为 owner/repository。");
    }
}

public sealed record UpdateManifest
{
    public int FormatVersion { get; init; } = 1;
    public string ProductId { get; init; } = "AcceleratorAccountDesk";
    public string Version { get; init; } = "";
    public string RuntimeId { get; init; } = "win-x64";
    public string ArchiveName { get; init; } = "";
    public long ArchiveSize { get; init; }
    public string Sha256 { get; init; } = "";
    public void Validate()
    {
        var parsed = UpdateVersion.Parse(Version);
        if (FormatVersion != 1 || ProductId != "AcceleratorAccountDesk" || RuntimeId != "win-x64"
            || Version != $"{parsed.Major}.{parsed.Minor}.{parsed.Build}"
            || ArchiveName != $"AcceleratorAccountDesk-{Version}-win-x64.zip" || ArchiveSize is <= 0 or > 536870912
            || !Regex.IsMatch(Sha256 ?? "", "^[0-9A-Fa-f]{64}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("更新清单的产品、版本、文件或完整性信息不匹配。");
    }
}

public sealed record AvailableUpdate(UpdateChannel Channel, UpdateManifest Manifest, Uri DownloadUri, string Notes);
public sealed record DownloadProgress(long ReceivedBytes, long TotalBytes, bool Verified = false)
{
    public double Percent => Verified ? 100 : TotalBytes == 0 ? 0 : Math.Min(99.9, ReceivedBytes * 100d / TotalBytes);
}
public sealed record UpdateInstallJob(string CurrentDirectory, string DataDirectory, string ArchivePath, UpdateManifest Manifest,
    string Repository, int ParentProcessId, long ParentStartTicks);
public sealed record UpdateInstallResult(bool Succeeded, string Version, string Message, string? InstalledDirectory = null);

public static class UpdateVersion
{
    public static Version Parse(string value)
    {
        if (!Regex.IsMatch(value ?? "", @"^v?[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant)
            || !Version.TryParse(value!.TrimStart('v'), out var parsed)) throw new InvalidDataException("更新版本格式无效，仅接受正式版本。");
        return new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
    }
}
