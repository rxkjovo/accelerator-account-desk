using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.Update;

internal static class UpdateFormChecks
{
    public static void Run(string root)
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action<string> action)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(directory); passed++; Console.WriteLine("PASS UPDATE FORM " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL UPDATE FORM " + name + ": " + error.Message); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        var channel = new UpdateChannel("fixture-owner/fixture-repo");
        var manifest = new UpdateManifest { Version = "0.3.4", ArchiveName = "AcceleratorAccountDesk-0.3.4-win-x64.zip", ArchiveSize = 3, Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })) };
        var release = JsonSerializer.Serialize(new { tag_name = "v0.3.4", draft = false, prerelease = false, body = "本地模拟更新说明\n所有请求均已拦截。", assets = new[] { new { name = "update-manifest.json", browser_download_url = "https://github.com/fixture-owner/fixture-repo/releases/download/v0.3.4/update-manifest.json", size = 1000L }, new { name = manifest.ArchiveName, browser_download_url = "https://github.com/fixture-owner/fixture-repo/releases/download/v0.3.4/" + manifest.ArchiveName, size = 3L } } });
        HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
        using var validHttp = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Json(release) : request.RequestUri.AbsolutePath.EndsWith(".json") ? Json(JsonSerializer.Serialize(manifest)) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) })));
        Check("newer release shows notes and enables the explicit install action", directory =>
        {
            var window = new UpdateWindow(directory, validHttp, channel, false);
            try
            {
                Click(window, "CheckButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消");
                Equal(true, Button(window, "InstallButton").IsEnabled); Equal("v0.3.4", Text(window, "LatestVersionText"));
                Equal(true, ((TextBox)window.FindName("NotesBox")).Text.Contains("所有请求均已拦截")); Equal(0, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
            }
            finally { window.Close(); }
        });
        Check("same version disables installation and reports current", directory =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(release.Replace("0.3.4", "0.3.3")))));
            var window = new UpdateWindow(directory, http, channel, false);
            try { Click(window, "CheckButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消"); Equal(false, Button(window, "InstallButton").IsEnabled); Equal("已是最新版本", Text(window, "HeadingText")); }
            finally { window.Close(); }
        });
        Check("unpublished source displays an error while preserving the current program", directory =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
            var window = new UpdateWindow(directory, http, channel, false);
            try { Click(window, "CheckButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消"); Equal(false, Button(window, "InstallButton").IsEnabled); Equal(true, Text(window, "StatusText").Contains("还没有可用的正式发布")); }
            finally { window.Close(); }
        });
        Check("pending check can be cancelled without enabling an install", directory =>
        {
            using var http = new HttpClient(new Handler(async (_, token) => { await Task.Delay(30000, token); return Json(release); }));
            var window = new UpdateWindow(directory, http, channel, false);
            try { Click(window, "CheckButton"); Equal("取消", Button(window, "CheckButton").Content?.ToString()); Click(window, "CheckButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消"); Equal(false, Button(window, "InstallButton").IsEnabled); Equal(true, Text(window, "StatusText").Contains("取消")); }
            finally { window.Close(); }
        });
        Check("a downloaded bad hash keeps the window open with no installable cache", directory =>
        {
            var window = new UpdateWindow(directory, validHttp, channel, false);
            try
            {
                Click(window, "CheckButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消");
                Click(window, "InstallButton"); Pump(() => Button(window, "CheckButton").Content?.ToString() != "取消");
                Equal(true, Text(window, "StatusText").Contains("完整性校验失败")); Equal(true, Button(window, "InstallButton").IsEnabled);
                Equal(0, Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length);
            }
            finally { window.Close(); }
        });
        Console.WriteLine($"RESULT UPDATE FORM {passed}/{passed + failed} passed; {failed} failed");
        if (failed != 0) throw new Exception("Update form checks failed.");
    }
    private static Button Button(Window window, string name) => (Button)window.FindName(name);
    private static string Text(Window window, string name) => ((TextBlock)window.FindName(name)).Text;
    private static void Click(Window window, string name) => Button(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void Pump(Func<bool> finished)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!finished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Update form did not settle.");
            var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
        }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken); }
}
