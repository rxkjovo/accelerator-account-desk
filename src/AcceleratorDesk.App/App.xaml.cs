using System.IO;
using System.Windows;
using AcceleratorDesk.Core;
using AcceleratorDesk.App.Services;

namespace AcceleratorDesk.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataDirectory = ResolveDataDirectory(e.Args);
            Directory.CreateDirectory(dataDirectory);
            var repository = new DeskRepository(Path.Combine(dataDirectory, "accounts.db"));
            repository.Initialize();
            var window = new MainWindow(repository, new SettingsStore(dataDirectory), dataDirectory);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：{ex.Message}\n\n请检查数据目录是否可读写。原有数据不会被自动覆盖。",
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
