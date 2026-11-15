using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AcceleratorDesk.Update;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => RunAsync(args).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1) return 2;
        UpdateInstallJob? job = null; Process? parent = null; Process? started = null; FileStream? lease = null;
        var trustedParent = false; var ready = false;
        try
        {
            var jobPath = Path.GetFullPath(args[0]); job = UpdateHandoff.ReadJob(jobPath);
            if (!UpdateHandoff.SamePath(AppContext.BaseDirectory, job.CurrentDirectory)) throw new InvalidDataException("请从当前程序启动更新。");
            parent = Process.GetProcessById(job.ParentProcessId);
            var path = parent.MainModule?.FileName ?? "";
            if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStartTicks
                || !(UpdateHandoff.SamePath(path, Path.Combine(job.CurrentDirectory, "加速账户助手.exe"))
                    || UpdateHandoff.SamePath(path, Path.Combine(job.CurrentDirectory, "AcceleratorAccountDesk.exe"))))
                throw new InvalidDataException("原程序进程信息已变化，未安装更新。");
            trustedParent = true;
            lease = new FileStream(Path.Combine(job.DataDirectory, "updates", "update-install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            UpdateHandoff.WriteJson(Path.Combine(Path.GetDirectoryName(jobPath)!, "ready.json"), new UpdateHelperReady(true, "更新程序已就绪。")); ready = true;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45))) await parent.WaitForExitAsync(timeout.Token);
            var target = UpdatePackage.Install(job.ArchivePath, job.Manifest, job.CurrentDirectory, job.Repository);
            started = StartApplication(target, job.DataDirectory, jobPath);
            await WaitForStartupAsync(started, jobPath, job.Manifest.Version);
            var shortcutWarning = "";
            try { UpdateOwnedShortcut(job, target); }
            catch { shortcutWarning = " 桌面快捷方式未能更新，可从新版文件夹打开。"; }
            WriteResult(job, new(true, job.Manifest.Version, "已更新至 v" + job.Manifest.Version + "，本地记录已保留。" + shortcutWarning, target));
            try { File.Delete(job.ArchivePath); } catch { }
            return 0;
        }
        catch (Exception error)
        {
            if (job is not null)
            {
                var message = error is OperationCanceledException ? "原程序仍在运行，更新已取消，请关闭其他窗口后重试。" : "更新未完成：" + error.Message;
                try
                {
                    if (!ready) UpdateHandoff.WriteJson(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "ready.json"), new UpdateHelperReady(false, message));
                    WriteResult(job, new(false, job.Manifest.Version, message + " 原版本和本地记录已保留。"));
                }
                catch { /* Cache write failure must never cause data or program deletion. */ }
                if (started is not null && !started.HasExited)
                {
                    try { started.CloseMainWindow(); using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await started.WaitForExitAsync(closeTimeout.Token); }
                    catch { }
                }
                if (trustedParent && parent is not null && parent.HasExited && (started is null || started.HasExited))
                    try { using var restored = StartApplication(job.CurrentDirectory, job.DataDirectory, null); } catch { }
            }
            return 1;
        }
        finally { lease?.Dispose(); parent?.Dispose(); started?.Dispose(); }
    }

    private static void WriteResult(UpdateInstallJob job, UpdateInstallResult result) => UpdateHandoff.WriteJson(Path.Combine(job.DataDirectory, "updates", "update-result.json"), result);

    private static Process StartApplication(string directory, string dataDirectory, string? ticket)
    {
        var info = new ProcessStartInfo(Path.Combine(directory, "加速账户助手.exe")) { UseShellExecute = false, WorkingDirectory = directory };
        info.ArgumentList.Add("--data-dir"); info.ArgumentList.Add(dataDirectory);
        if (ticket is not null) info.Environment[UpdateHandoff.StartupTicketVariable] = ticket;
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动程序，请从旧版本文件夹重新打开。");
    }

    private static async Task WaitForStartupAsync(Process process, string jobPath, string version)
    {
        var receiptPath = Path.Combine(Path.GetDirectoryName(jobPath)!, "started.json"); var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(35))
        {
            if (File.Exists(receiptPath))
            {
                var receipt = JsonSerializer.Deserialize<UpdateStartupReceipt>(await File.ReadAllTextAsync(receiptPath));
                if (receipt is not null && receipt.ProcessId == process.Id && receipt.Version == version)
                {
                    if (!receipt.Succeeded) throw new InvalidOperationException(receipt.Message);
                    if (!process.HasExited) return;
                }
            }
            if (process.HasExited) throw new InvalidOperationException("新版没有正常打开，正在恢复原版本。");
            await Task.Delay(200);
        }
        throw new TimeoutException("新版启动等待超时。");
    }

    private static void UpdateOwnedShortcut(UpdateInstallJob job, string target)
    {
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "加速账户助手.lnk");
        if (!File.Exists(shortcutPath)) return;
        object? shell = null; object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("快捷方式组件不可用。"));
            dynamic comShell = shell!; shortcut = comShell.CreateShortcut(shortcutPath); dynamic link = shortcut;
            string oldTarget = link.TargetPath;
            if (!UpdateHandoff.SamePath(oldTarget, Path.Combine(job.CurrentDirectory, "加速账户助手.exe"))
                && !UpdateHandoff.SamePath(oldTarget, Path.Combine(job.CurrentDirectory, "AcceleratorAccountDesk.exe"))) return;
            var executable = Path.Combine(target, "加速账户助手.exe");
            link.TargetPath = executable; link.WorkingDirectory = target; link.IconLocation = executable + ",0";
            link.Arguments = "--data-dir \"" + job.DataDirectory.TrimEnd('\\') + (job.DataDirectory.EndsWith('\\') ? "\\\\" : "") + "\"";
            link.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
