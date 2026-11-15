using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using AcceleratorDesk.Update;

namespace AcceleratorDesk.App.Dialogs;

public partial class UpdateWindow : Window
{
    private readonly string _dataDirectory;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly UpdateChannel? _channel;
    private readonly Version _currentVersion = typeof(App).Assembly.GetName().Version!;
    private CancellationTokenSource? _operation;
    private AvailableUpdate? _available;
    private bool _working;
    private bool _closing;

    public UpdateWindow(string dataDirectory, HttpClient? httpClient = null, UpdateChannel? channel = null, bool checkOnOpen = true)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory); _channel = channel;
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
        InitializeComponent();
        CurrentVersionText.Text = "v" + _currentVersion.ToString(3);
        if (checkOnOpen) Loaded += async (_, _) => await CheckAsync();
        Closing += OnClosing;
        Closed += (_, _) => { _closing = true; _operation?.Cancel(); if (_ownsHttp) _http.Dispose(); };
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_working) { _operation?.Cancel(); return; }
        await CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_working) return;
        BeginOperation(); _available = null; LatestVersionText.Text = "正在检查";
        StatusText.Text = "正在检查 GitHub 最新正式发布…"; NotesBox.Text = "正在读取更新说明…";
        try
        {
            _operation!.CancelAfter(TimeSpan.FromSeconds(30));
            var channel = _channel ?? UpdateChannel.Load(AppContext.BaseDirectory);
            _available = await new UpdateClient(_http).CheckAsync(channel, _currentVersion, _operation.Token);
            if (_available is null)
            {
                HeadingText.Text = "已是最新版本"; LatestVersionText.Text = CurrentVersionText.Text;
                StatusText.Text = "当前版本可以继续使用。"; NotesBox.Text = "暂时没有比当前版本更新的正式发布。";
            }
            else
            {
                HeadingText.Text = "发现新版本"; LatestVersionText.Text = "v" + _available.Manifest.Version;
                NotesBox.Text = string.IsNullOrWhiteSpace(_available.Notes) ? "本次发布没有提供更新说明。" : _available.Notes;
                StatusText.Text = $"更新包 {Megabytes(_available.Manifest.ArchiveSize)} MB。点击「下载并更新」后，软件会下载、校验并重新打开。";
            }
        }
        catch (Exception error)
        {
            LatestVersionText.Text = "未完成检查"; NotesBox.Text = "可继续使用当前版本，稍后重新检查。";
            StatusText.Text = FriendlyError(error);
        }
        finally { EndOperation(); }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_working || _available is null) return;
        BeginOperation(); DownloadProgressBar.Visibility = Visibility.Visible; DownloadProgressBar.Value = 0;
        StatusText.Text = "正在下载更新包…";
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                if (_closing) return;
                DownloadProgressBar.Value = value.Percent;
                StatusText.Text = value.Verified ? "完整性校验通过，正在准备安装…" : $"已下载 {Megabytes(value.ReceivedBytes)} / {Megabytes(value.TotalBytes)} MB";
            });
            var archive = await new UpdateClient(_http).DownloadAsync(_available, Path.Combine(_dataDirectory, "updates", "downloads"), progress, _operation!.Token);
            _operation.Token.ThrowIfCancellationRequested();
            var directory = Path.Combine(_dataDirectory, "updates", "job-" + Guid.NewGuid().ToString("N"));
            var jobPath = Path.Combine(directory, "job.json");
            using var process = Process.GetCurrentProcess();
            var job = new UpdateInstallJob(Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)), _dataDirectory, archive, _available.Manifest, _available.Channel.Repository, process.Id, process.StartTime.ToUniversalTime().Ticks);
            UpdateHandoff.WriteJson(jobPath, job);
            var helper = Path.Combine(AppContext.BaseDirectory, "AcceleratorDesk.Updater.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("当前版本缺少更新程序，请下载完整分享包。");
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(jobPath);
            using var updateProcess = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立更新程序。");
            var readyPath = Path.Combine(directory, "ready.json"); var elapsed = Stopwatch.StartNew();
            // After handoff starts, keep this process open until the helper confirms it can wait for us.
            CheckButton.IsEnabled = false; CloseButton.IsEnabled = false;
            while (!File.Exists(readyPath))
            {
                if (updateProcess.HasExited || elapsed.Elapsed > TimeSpan.FromSeconds(10)) throw new InvalidOperationException("更新程序未能就绪，当前软件继续运行。请稍后重试。");
                await Task.Delay(100);
            }
            var ready = JsonSerializer.Deserialize<UpdateHelperReady>(await File.ReadAllTextAsync(readyPath));
            if (ready?.Ready != true) throw new InvalidOperationException(ready?.Message ?? "更新程序没有确认就绪。");
            StatusText.Text = "正在重新打开新版…";
            _working = false; Application.Current.Shutdown();
        }
        catch (Exception error) { StatusText.Text = FriendlyError(error); }
        finally { if (!_closing) EndOperation(); }
    }

    private void BeginOperation()
    {
        _working = true; _operation?.Dispose(); _operation = new CancellationTokenSource();
        InstallButton.IsEnabled = false; CheckButton.Content = "取消"; CheckButton.IsEnabled = true;
    }
    private void EndOperation()
    {
        _working = false; CheckButton.Content = "重新检查"; CheckButton.IsEnabled = true; CloseButton.IsEnabled = true;
        InstallButton.IsEnabled = _available is not null; _operation?.Dispose(); _operation = null;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_working && !CloseButton.IsEnabled) { e.Cancel = true; return; }
        _operation?.Cancel();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private static string Megabytes(long bytes) => (bytes / 1048576d).ToString("0.0");
    private static string FriendlyError(Exception error) => error switch
    {
        OperationCanceledException => "操作已取消或连接超时，可以继续使用当前版本。",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "GitHub 暂时限制了请求次数，请稍后再试。",
        HttpRequestException => "暂时无法连接 GitHub，请检查网络后重试。",
        _ => error.Message
    };
}
