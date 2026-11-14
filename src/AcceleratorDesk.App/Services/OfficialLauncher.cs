using System.Diagnostics;
using System.IO;

namespace AcceleratorDesk.App.Services;

public static class OfficialLauncher
{
    public static void OpenQqRegistration() => Process.Start(new ProcessStartInfo(
        "https://ssl.zc.qq.com/v5/index/?lang=zhCN") { UseShellExecute = true });

    public static void OpenDataDirectory(string directory) => Process.Start(new ProcessStartInfo("explorer.exe")
    {
        ArgumentList = { Path.GetFullPath(directory) }, UseShellExecute = true
    });

    public static void OpenClient(string executable)
    {
        var path = Path.GetFullPath(executable);
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请在设置中选择本机迅雷游戏加速器的 exe 文件。");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
    }
}
