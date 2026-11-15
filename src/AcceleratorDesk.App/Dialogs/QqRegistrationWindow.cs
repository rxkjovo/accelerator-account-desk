using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AcceleratorDesk.App.Dialogs;

public sealed class QqRegistrationWindow : Window
{
    private readonly DeskRepository _repository;
    private readonly string _dataDirectory;
    private QqRegistrationSession _session;
    private readonly ComboBox _phone = new();
    private readonly TextBox _code = new() { MaxLength = 6, FontSize = 20 };
    private readonly TextBox _qq = new();
    private readonly CheckBox _confirmed = new() { Content = "我已看到官方注册成功，并核对了 QQ 号" };
    private readonly TextBox _nickname = new() { IsReadOnly = true };
    private readonly PasswordBox _password = new();
    private readonly TextBox _visiblePassword = new() { IsReadOnly = true, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
    private readonly TextBlock _browserNotice = new() { Text = "正在加载 QQ 官方页面…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly WebView2 _browser = new() { DefaultBackgroundColor = System.Drawing.Color.White };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Button _start, _submit, _read, _manualSave;
    private readonly List<Button> _operations = [];
    private bool _busy, _closed, _ready, _polling;
    private DateTimeOffset? _lastSmsRequest;
    private DateTimeOffset? _pollStarted;
    private Task? _initialization;
    public Guid? SavedAccountId => _session.SavedAccountId;

    public QqRegistrationWindow(DeskRepository repository, string dataDirectory)
    {
        _repository = repository; _dataDirectory = dataDirectory; _session = new(repository, dataDirectory);
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AcceleratorAccountDesk;component/Theme.Dark.xaml", UriKind.Relative) });
        Style = (Style)FindResource(typeof(Window));
        Title = "注册 QQ · 加速账户助手"; Width = 1240; Height = 820; MinWidth = 1050; MinHeight = 670;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; WindowStyle = WindowStyle.None;
        UseLayoutRounding = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 42, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var caption = new Grid { Background = (Brush)FindResource("Panel") };
        caption.Children.Add(new TextBlock { Text = "加速账户助手  /  QQ 注册", Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = (Brush)FindResource("Muted") });
        var close = new Button { Content = "\uE8BB", Style = (Style)FindResource("CaptionButton"), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "关闭注册向导"); WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => Close(); caption.Children.Add(close); root.Children.Add(caption);
        var columns = new Grid { Margin = new Thickness(24) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(columns, 1); root.Children.Add(columns);
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "注册 QQ", FontSize = 26, FontWeight = FontWeights.SemiBold });
        body.Children.Add(Muted("选号码，填验证码，其余交给软件。", 0, 7, 0, 22));
        body.Children.Add(Label("01  选择手机号"));
        AutomationProperties.SetName(_phone, "注册手机号");
        var phoneRow = new Grid(); phoneRow.ColumnDefinitions.Add(new ColumnDefinition()); phoneRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        phoneRow.Children.Add(_phone);
        var add = Operation("添加", () => AddPhone(), primary: false); add.Margin = new Thickness(8, 0, 0, 0); add.Padding = new Thickness(13, 7, 13, 7); Grid.SetColumn(add, 1); phoneRow.Children.Add(add); body.Children.Add(phoneRow);
        _start = Operation("开始注册 / 获取验证码", () => _ = RunAsync(StartAsync)); _start.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(_start);
        body.Children.Add(Muted("自动生成昵称和独立密码。额外安全验证在右侧官方页面完成。", 0, 8, 0, 24));
        body.Children.Add(Label("02  输入短信验证码")); AutomationProperties.SetName(_code, "短信验证码"); body.Children.Add(_code);
        _submit = Operation("同意协议并注册", () => _ = RunAsync(SubmitAsync)); _submit.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(_submit);
        body.Children.Add(Muted("点击上方按钮即表示你已阅读并同意 QQ 的：", 0, 9, 0, 0));
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        links.Children.Add(Operation("服务协议", () => NavigateOfficial("https://ti.qq.com/agreement/index.html"), false, quiet: true));
        links.Children.Add(Operation("隐私政策", () => NavigateOfficial("https://ti.qq.com/doc/redirect/qq-privacy-guild"), false, quiet: true)); body.Children.Add(links);
        var statusBorder = new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(13), Margin = new Thickness(0, 16, 0, 15), Child = _status };
        AutomationProperties.SetName(_status, "注册进度"); body.Children.Add(statusBorder);
        var extra = new StackPanel();
        extra.Children.Add(Label("自动生成的昵称")); extra.Children.Add(_nickname);
        extra.Children.Add(new TextBlock { Text = "本次密码", Style = (Style)FindResource("FieldLabel"), Margin = new Thickness(0, 12, 0, 7) });
        _password.IsEnabled = false; extra.Children.Add(_password); extra.Children.Add(_visiblePassword);
        AutomationProperties.SetName(_password, "自动生成的密码"); AutomationProperties.SetName(_visiblePassword, "已显示的注册密码");
        var reveal = new CheckBox { Content = "显示本次密码", Margin = new Thickness(0, 12, 0, 10) };
        reveal.Checked += (_, _) => { _visiblePassword.Text = _password.Password; _visiblePassword.Visibility = Visibility.Visible; };
        reveal.Unchecked += (_, _) => { _visiblePassword.Clear(); _visiblePassword.Visibility = Visibility.Collapsed; }; extra.Children.Add(reveal);
        extra.Children.Add(Operation("复制本次密码", () => { SecretClipboard.Copy(_password.Password); SetStatus("密码已复制；30 秒后会尝试清除未被替换的剪贴板内容。"); }, false));
        extra.Children.Add(Operation("放弃当前草稿并重新选择", ResetDraft, false));
        body.Children.Add(new Expander { Header = "查看本次资料与恢复草稿", Content = extra, Margin = new Thickness(0, 0, 0, 14) });
        var manual = new StackPanel();
        manual.Children.Add(Muted("自动读取不成功时，将官方成功页的 QQ 号粘贴到这里。", 0, 10, 0, 10));
        AutomationProperties.SetName(_qq, "注册成功的 QQ 号"); manual.Children.Add(_qq);
        _confirmed.Margin = new Thickness(0, 12, 0, 12); AutomationProperties.SetName(_confirmed, "已核对官方注册成功"); manual.Children.Add(_confirmed);
        _manualSave = Operation("保存已确认的账号与密码", SaveManual, false); manual.Children.Add(_manualSave);
        body.Children.Add(new Expander { Header = "结果未读取？手动确认", Content = manual });
        columns.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) });
        var browserPanel = new Grid(); browserPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); browserPanel.RowDefinitions.Add(new RowDefinition());
        var browserHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) }; browserHeader.ColumnDefinitions.Add(new ColumnDefinition()); browserHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        browserHeader.Children.Add(new TextBlock { Text = "QQ 官方页面  ·  ssl.zc.qq.com", FontSize = 12, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
        var browserTools = new StackPanel { Orientation = Orientation.Horizontal };
        browserTools.Children.Add(Operation("返回注册页", () => _ = RunAsync(ReloadBrowserAsync), false, quiet: true));
        browserTools.Children.Add(Operation("系统浏览器", OfficialLauncher.OpenQqRegistration, false, quiet: true));
        _read = Operation("读取结果", () => _ = RunAsync(CheckResultAsync), false, quiet: true); browserTools.Children.Add(_read);
        Grid.SetColumn(browserTools, 1); browserHeader.Children.Add(browserTools); browserPanel.Children.Add(browserHeader);
        var page = new Grid { Background = Brushes.White }; page.Children.Add(_browser); page.Children.Add(_browserNotice);
        var frame = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), Child = page };
        Grid.SetRow(frame, 1); browserPanel.Children.Add(frame); Grid.SetColumn(browserPanel, 2); columns.Children.Add(browserPanel);
        Content = new Border { Background = (Brush)FindResource("Bg"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), Child = root };
        RefreshPhones(); RefreshDraft();
        _poll.Tick += async (_, _) =>
        {
            if (_busy || _polling || _closed) return;
            if (_pollStarted is { } started && DateTimeOffset.Now - started > TimeSpan.FromMinutes(2)) { _poll.Stop(); SetStatus("暂未读取到明确成功结果。资料已加密保留，可在右侧继续验证，或使用下方手动确认。"); return; }
            _polling = true;
            try { await CheckResultAsync(); }
            catch (Exception ex) { if (!_closed) { _poll.Stop(); SetStatus("结果尚未保存：" + ex.Message); } }
            finally { _polling = false; }
        };
        Loaded += async (_, _) => await RunAsync(EnsureBrowserAsync);
        Closed += (_, _) => { _closed = true; _poll.Stop(); _password.Clear(); _visiblePassword.Clear(); _code.Clear(); _browser.Dispose(); };
    }

    private Button Operation(string text, Action action, bool primary = true, bool quiet = false)
    {
        var button = new Button { Content = text, MinHeight = quiet ? 28 : 40, Margin = quiet ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 8) };
        button.Style = (Style)FindResource(quiet ? "QuietButton" : primary ? "PrimaryButton" : typeof(Button));
        if (quiet) { button.Padding = new Thickness(4, 4, 4, 4); button.FontSize = 12; }
        button.Click += (_, _) => { try { action(); } catch (Exception ex) { SetStatus(ex.Message); } };
        _operations.Add(button); return button;
    }
    private TextBlock Label(string text) => new() { Text = text, Style = (Style)FindResource("FieldLabel") };
    private TextBlock Muted(string text, double left, double top, double right, double bottom) => new() { Text = text, Style = (Style)FindResource("MutedText"), FontSize = 12, Margin = new Thickness(left, top, right, bottom) };
    private void SetStatus(string text) { if (!_closed) _status.Text = text; }

    private void RefreshPhones(Guid? selected = null)
    {
        _phone.ItemsSource = _repository.GetPhones().Select(x => new Choice<Guid>(x.Id, $"{(string.IsNullOrWhiteSpace(x.Label) ? "本人号码" : x.Label)}  {AccountPresentation.MaskPhone(x.Number)}")).ToList();
        var id = selected ?? _session.Draft?.PhoneId;
        _phone.SelectedItem = _phone.Items.OfType<Choice<Guid>>().FirstOrDefault(x => x.Value == id) ?? _phone.Items.OfType<Choice<Guid>>().FirstOrDefault();
    }
    private void AddPhone()
    {
        var dialog = new PhoneDialog(null, _repository.SavePhone) { Owner = this };
        if (dialog.ShowDialog() == true) RefreshPhones(_repository.GetPhones().Last().Id);
    }
    private void RefreshDraft()
    {
        _nickname.Text = _session.Draft?.Nickname ?? "开始后自动生成";
        _password.Password = _session.Draft?.Password ?? "";
        _phone.IsEnabled = _session.Draft is null;
        SetStatus(_session.Draft is null ? "准备就绪。选择手机号后开始注册。" : "已恢复待确认草稿。昵称与密码保持原值，请先核对本次注册结果。");
        RefreshButtons();
    }
    private void RefreshButtons()
    {
        foreach (var button in _operations) button.IsEnabled = !_busy;
        _start.IsEnabled = !_busy && SavedAccountId is null;
        _submit.IsEnabled = !_busy && _session.Draft is not null && SavedAccountId is null;
        _manualSave.IsEnabled = !_busy && _session.Draft is not null && SavedAccountId is null;
        _read.IsEnabled = !_busy && _ready;
        _code.IsEnabled = !_busy && SavedAccountId is null;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closed) return;
        _busy = true; RefreshButtons();
        try { await action(); }
        catch (Exception ex) { if (!_closed) SetStatus("操作未完成：" + ex.Message); }
        finally { _busy = false; if (!_closed) RefreshButtons(); }
    }

    private Task EnsureBrowserAsync() => _initialization ??= InitializeBrowserAsync();
    private async Task InitializeBrowserAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_dataDirectory, "qq-browser"));
            if (_closed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (_closed) return;
            var core = _browser.CoreWebView2;
            core.Settings.IsWebMessageEnabled = false; core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false;
            core.Profile.IsPasswordAutosaveEnabled = false; core.Profile.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) => { if (!QqRegistrationPolicy.IsOfficialNavigation(e.Uri)) { e.Cancel = true; SetStatus("此跳转不属于 QQ 官方 HTTPS 页面，已停止。请返回注册页继续。"); } };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (QqRegistrationPolicy.IsOfficialNavigation(e.Uri)) core.Navigate(e.Uri); else SetStatus("请在 QQ 官方页面完成本次注册。"); };
            core.NavigationCompleted += (_, e) =>
            {
                if (_closed) return;
                _ready = e.IsSuccess; _browserNotice.Visibility = e.IsSuccess ? Visibility.Collapsed : Visibility.Visible;
                if (!e.IsSuccess) { _browserNotice.Text = "QQ 页面未能加载。可点击返回注册页重试；当前草稿仍已保留。"; SetStatus("官方页面加载失败：" + e.WebErrorStatus); }
                RefreshButtons();
            };
            core.Navigate(QqRegistrationPolicy.RegistrationUrl);
        }
        catch
        {
            _initialization = null;
            _browserNotice.Text = "无法启动内嵌浏览器。请确认已安装 Microsoft Edge WebView2 Runtime；也可在系统浏览器完成后手动确认结果。";
            throw;
        }
    }

    private async Task ReloadBrowserAsync()
    {
        await EnsureBrowserAsync();
        if (_closed) return;
        _ready = false; _browserNotice.Text = "正在加载 QQ 官方页面…"; _browserNotice.Visibility = Visibility.Visible;
        _browser.CoreWebView2?.Navigate(QqRegistrationPolicy.RegistrationUrl);
    }
    private void NavigateOfficial(string url)
    {
        if (_browser.CoreWebView2 is null) { SetStatus("请先等待官方页面载入。"); return; }
        _browser.CoreWebView2.Navigate(url);
    }
    private async Task<JsonElement> ExecuteAsync(string script)
    {
        if (!_ready || _browser.CoreWebView2 is null) throw new InvalidOperationException("请等待右侧官方页面载入后重试。");
        if (!QqRegistrationPolicy.IsRegistrationPage(_browser.Source?.AbsoluteUri)) throw new InvalidOperationException("请先点击返回注册页，资料只会填写到固定 QQ 注册入口。");
        var json = await _browser.ExecuteScriptAsync(script);
        var result = JsonDocument.Parse(json).RootElement.Clone();
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            throw new PageActionRejectedException(result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message", out var message) ? message.GetString() : "页面尚不能执行此操作，请在右侧核对官方提示。");
        return result;
    }

    private async Task StartAsync()
    {
        if (_session.Draft is { SubmissionAttempted: true }) { await CheckResultAsync(); if (SavedAccountId is null) SetStatus("当前已有提交待确认。请核对右侧结果或手动补录，不会重复请求注册。"); return; }
        if (_lastSmsRequest is { } previous && DateTimeOffset.Now - previous < TimeSpan.FromSeconds(60)) throw new InvalidOperationException("请先等待短信或完成右侧安全验证，至少 60 秒后才能手动重试。");
        if (_phone.SelectedItem is not Choice<Guid> choice) throw new ArgumentException("请先添加并选择一个本人手机号。");
        var phone = _repository.GetPhones().Single(x => x.Id == choice.Value);
        var draft = _session.Prepare(phone); RefreshDraft();
        await EnsureBrowserAsync();
        await ExecuteAsync(QqPageBridge.Fill(draft.PhoneNumber, draft.Nickname, draft.Password));
        await Task.Delay(120);
        var result = await ExecuteAsync(QqPageBridge.RequestSms());
        _lastSmsRequest = DateTimeOffset.Now;
        SetStatus(result.GetProperty("message").GetString() ?? "已请求验证码，请查看短信和官方验证提示。");
    }

    private async Task SubmitAsync()
    {
        var draft = _session.Draft ?? throw new InvalidOperationException("请先选择号码并开始注册。");
        if (!System.Text.RegularExpressions.Regex.IsMatch(_code.Text.Trim(), @"^[0-9]{6}$")) throw new ArgumentException("请填写短信中的 6 位验证码。");
        if (draft.SubmissionAttempted) throw new InvalidOperationException("本次已提交，请先查看官方结果或手动确认，避免重复提交。");
        await ExecuteAsync(QqPageBridge.Fill(draft.PhoneNumber, draft.Nickname, draft.Password));
        await ExecuteAsync(QqPageBridge.PrepareSubmission(_code.Text.Trim(), userAcceptedAgreement: true));
        await Task.Delay(120);
        _session.MarkSubmitted();
        try { await ExecuteAsync(QqPageBridge.Submit(draft.PhoneNumber)); }
        catch (PageActionRejectedException) { _session.ReleaseUnsentSubmission(); throw; }
        _code.Clear(); _pollStarted = DateTimeOffset.Now; _poll.Start();
        SetStatus("已提交，正在等待官方结果；若出现安全验证，请在右侧完成。明确成功后将自动加密保存。");
        await CheckResultAsync();
    }

    private async Task CheckResultAsync()
    {
        if (_session.Draft is null || SavedAccountId is not null) return;
        var result = await ExecuteAsync(QqPageBridge.VisibleText());
        var text = result.GetProperty("text").GetString() ?? "";
        if (_session.TrySaveOfficialResult(_browser.Source?.AbsoluteUri ?? "", text)) ShowSaved();
        else if (_session.AllowCorrectedCodeRetry(_browser.Source?.AbsoluteUri ?? "", text))
        {
            _poll.Stop(); _code.Clear(); SetStatus("官方提示验证码错误或已失效。请核对后重新填写验证码，再点击注册；昵称和密码保持不变。");
        }
        else SetStatus("尚未读取到明确的注册成功和 QQ 号。可继续完成官方验证，或展开下方手动确认。");
    }
    private void SaveManual()
    {
        _session.ConfirmManual(_qq.Text, _confirmed.IsChecked == true); ShowSaved();
    }
    private void ShowSaved()
    {
        _poll.Stop();
        var account = _repository.GetAccounts().Single(x => x.Id == SavedAccountId);
        _qq.Text = account.Identifier; _code.Clear();
        SetStatus($"已保存 QQ：{account.Identifier}\n关联手机号和加密密码已保存，可返回账号列表查看。" + (_session.CleanupWarning.Length > 0 ? "\n" + _session.CleanupWarning : ""));
        RefreshButtons();
    }
    private void ResetDraft()
    {
        if (_session.Draft is null || SavedAccountId is not null) return;
        if (MessageBox.Show(this, "放弃草稿会删除本次自动生成的昵称和密码。若 QQ 已注册成功，请先保存账号或复制密码。确定放弃？", "放弃当前注册草稿", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _poll.Stop(); new QqRegistrationDraftStore(_dataDirectory).Clear(); _session = new(_repository, _dataDirectory);
        _code.Clear(); _qq.Clear(); _visiblePassword.Clear(); _confirmed.IsChecked = false; _lastSmsRequest = null;
        RefreshPhones(); RefreshDraft(); _ = RunAsync(ReloadBrowserAsync);
    }

    private sealed class PageActionRejectedException(string? message) : InvalidOperationException(message);
}
