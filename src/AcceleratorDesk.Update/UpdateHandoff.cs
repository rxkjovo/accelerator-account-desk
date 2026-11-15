using System.Text.Json;
using System.Text.RegularExpressions;

namespace AcceleratorDesk.Update;

public sealed record UpdateHelperReady(bool Ready, string Message);
public sealed record UpdateStartupReceipt(bool Succeeded, int ProcessId, string Version, string Message);

public static class UpdateHandoff
{
    public const string StartupTicketVariable = "ACCELERATORDESK_UPDATE_TICKET";

    public static UpdateInstallJob ReadJob(string jobPath)
    {
        jobPath = Path.GetFullPath(jobPath);
        var file = new FileInfo(jobPath);
        if (!file.Exists || file.Length is <= 0 or > 65536) throw new InvalidDataException("更新任务不存在或内容异常。");
        UpdateInstallJob job;
        try { job = JsonSerializer.Deserialize<UpdateInstallJob>(File.ReadAllText(jobPath)) ?? throw new InvalidDataException("更新任务为空。"); }
        catch (JsonException) { throw new InvalidDataException("更新任务无法读取。"); }
        if (job.Manifest is null) throw new InvalidDataException("更新任务缺少清单。");
        job.Manifest.Validate(); new UpdateChannel(job.Repository).Validate();
        if (!Absolute(job.CurrentDirectory) || !Absolute(job.DataDirectory) || !Absolute(job.ArchivePath)
            || job.ParentProcessId <= 0 || job.ParentStartTicks <= 0) throw new InvalidDataException("更新任务路径或进程信息无效。");
        var updates = Path.Combine(job.DataDirectory, "updates");
        var taskDirectory = Path.GetDirectoryName(jobPath)!;
        if (!SamePath(Path.GetDirectoryName(taskDirectory)!, updates)
            || !Regex.IsMatch(Path.GetFileName(taskDirectory), "^job-[a-f0-9]{32}$", RegexOptions.CultureInvariant)
            || Path.GetFileName(jobPath) != "job.json"
            || !SamePath(Path.GetDirectoryName(job.ArchivePath)!, Path.Combine(updates, "downloads")))
            throw new InvalidDataException("更新任务不在本机更新缓存中。");
        return job;
    }

    public static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool Absolute(string value) => !string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value) && SamePath(value, Path.GetFullPath(value));

    public static string TargetDirectory(UpdateInstallJob job) => Path.Combine(Directory.GetParent(job.CurrentDirectory)?.FullName ?? throw new InvalidDataException("原程序目录无效。"), "加速账户助手-" + job.Manifest.Version);

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(value)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
