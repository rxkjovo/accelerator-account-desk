using System.IO;
using System.Windows;
using AcceleratorDesk.Core;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Update;
using System.Text.Json;

namespace AcceleratorDesk.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var updateTicket = Environment.GetEnvironmentVariable(UpdateHandoff.StartupTicketVariable);
        Environment.SetEnvironmentVariable(UpdateHandoff.StartupTicketVariable, null);
        UpdateInstallJob? updateJob = null;
        try
        {
            var dataDirectory = ResolveDataDirectory(e.Args);
            if (!string.IsNullOrEmpty(updateTicket))
            {
                updateJob = UpdateHandoff.ReadJob(updateTicket);
                if (!UpdateHandoff.SamePath(dataDirectory, updateJob.DataDirectory)
                    || !UpdateHandoff.SamePath(AppContext.BaseDirectory, UpdateHandoff.TargetDirectory(updateJob))
                    || typeof(App).Assembly.GetName().Version != UpdateVersion.Parse(updateJob.Manifest.Version))
                    throw new InvalidDataException("新版启动信息与更新任务不一致。");
            }
            Directory.CreateDirectory(dataDirectory);
            var repository = new DeskRepository(Path.Combine(dataDirectory, "accounts.db"));
            repository.Initialize();
            var window = new MainWindow(repository, new SettingsStore(dataDirectory), dataDirectory);
            MainWindow = window;
            if (updateJob is not null)
            {
                var ticketJob = updateJob;
                window.ContentRendered += (_, _) =>
                {
                    UpdateHandoff.WriteJson(Path.Combine(Path.GetDirectoryName(updateTicket!)!, "started.json"), new UpdateStartupReceipt(true, Environment.ProcessId, ticketJob.Manifest.Version, "新版主窗口已打开。"));
                    window.ShowUpdateMessage("已更新至 v" + ticketJob.Manifest.Version + "，本地账号和密码记录继续保留。");
                };
            }
            else
            {
                var resultPath = Path.Combine(dataDirectory, "updates", "update-result.json");
                if (File.Exists(resultPath))
                {
                    try
                    {
                        var result = JsonSerializer.Deserialize<UpdateInstallResult>(File.ReadAllText(resultPath));
                        if (result?.Succeeded == false) window.ShowUpdateMessage(result.Message);
                    }
                    catch (Exception error) when (error is JsonException or IOException) { }
                }
            }
            window.Show();
        }
        catch (Exception ex)
        {
            if (updateJob is not null)
            {
                try { UpdateHandoff.WriteJson(Path.Combine(Path.GetDirectoryName(updateTicket!)!, "started.json"), new UpdateStartupReceipt(false, Environment.ProcessId, updateJob.Manifest.Version, "新版启动失败：" + ex.Message)); }
                catch { }
            }
            else MessageBox.Show($"启动失败：{ex.Message}\n\n请检查数据目录是否可读写。原有数据不会被自动覆盖。",
                    "加速账户助手", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string ResolveDataDirectory(string[] args)
    {
        if (args.Length == 0)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AcceleratorAccountDesk");
        if (args.Length == 2 && args[0] == "--data-dir" && !string.IsNullOrWhiteSpace(args[1]))
            return Path.GetFullPath(args[1]);
        throw new ArgumentException("启动参数格式：--data-dir \"本地数据目录\"");
    }
}
