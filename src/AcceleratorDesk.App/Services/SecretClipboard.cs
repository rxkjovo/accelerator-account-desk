using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AcceleratorDesk.App.Services;

public static class SecretClipboard
{
    private static string? _copied;
    private static DispatcherTimer? _timer;
    private static bool _exitAttached;

    public static void Copy(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("尚未保存密码。");
        Clipboard.SetText(password);
        _copied = password;
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => ClearIfUnchanged();
        _timer.Start();
        if (!_exitAttached && Application.Current is { } app)
        {
            app.Exit += (_, _) => ClearIfUnchanged();
            _exitAttached = true;
        }
    }

    private static void ClearIfUnchanged()
    {
        _timer?.Stop();
        try { if (_copied is not null && Clipboard.ContainsText() && Clipboard.GetText() == _copied) Clipboard.Clear(); }
        catch (ExternalException) { /* Another application may hold the clipboard. */ }
        finally { _copied = null; }
    }
}
