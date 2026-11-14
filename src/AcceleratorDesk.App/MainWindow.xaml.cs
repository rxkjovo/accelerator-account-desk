using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App;

public partial class MainWindow : Window
{
    private readonly DeskRepository _repository;
    private readonly SettingsStore _settingsStore;
    private readonly string _dataDirectory;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DeskSettings _settings;
    private IReadOnlyList<AccountRecord> _accounts = [];
    private IReadOnlyList<PhoneRecord> _phones = [];
    private IReadOnlyList<RedemptionRecord> _redemptions = [];
    private List<AccountRow> _visibleAccounts = [];
    private bool _ready;
    private string _page = "accounts";

    public MainWindow(DeskRepository repository, SettingsStore settingsStore, string dataDirectory)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _dataDirectory = dataDirectory;
        _settings = settingsStore.Load();
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(1320, SystemParameters.WorkArea.Width - 30));
        Height = Math.Max(MinHeight, Math.Min(820, SystemParameters.WorkArea.Height - 35));
        ProviderFilter.ItemsSource = new[] { new Choice<AccountProvider?>(null, "全部渠道"), new Choice<AccountProvider?>(AccountProvider.QQ, "QQ"), new Choice<AccountProvider?>(AccountProvider.Weibo, "微博") };
        ProviderFilter.SelectedIndex = 0;
        MembershipFilter.ItemsSource = new[] { "全部时长", "使用中", "即将到期", "已到期", "未记录到期" };
        MembershipFilter.SelectedIndex = 0;
        ClientPathBox.Text = _settings.ClientExecutable;
        DataPathText.Text = _dataDirectory;
        _ready = true;
        Reload();
        Navigate("accounts");
        _timer.Tick += (_, _) => { ApplyAccountFilters(); UpdateMetrics(); };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void Reload(Guid? selectAccount = null)
    {
        var selectedId = selectAccount ?? (AccountsGrid.SelectedItem as AccountRow)?.Record.Id;
        _phones = _repository.GetPhones();
        _accounts = _repository.GetAccounts();
        _redemptions = _repository.GetRedemptions();
        ApplyAccountFilters(selectedId);
        PhonesGrid.ItemsSource = _phones.Select(x => new PhoneRow(x, _accounts)).ToList();
        var accountMap = _accounts.ToDictionary(x => x.Id);
        RedemptionsGrid.ItemsSource = _redemptions.Select(x => new RedemptionRow(x, accountMap)).ToList();
        PhoneEmpty.Visibility = _phones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RedemptionEmpty.Visibility = _redemptions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateMetrics();
        SetStatus($"本地记录已读取 · {DateTime.Now:HH:mm}");
    }

    private void ApplyAccountFilters(Guid? selectedId = null)
    {
        if (!_ready) return;
        selectedId ??= (AccountsGrid.SelectedItem as AccountRow)?.Record.Id;
        var phoneMap = _phones.ToDictionary(x => x.Id);
        var term = AccountSearch.Text.Trim();
        var provider = (ProviderFilter.SelectedItem as Choice<AccountProvider?>)?.Value;
        var membership = MembershipFilter.SelectedItem as string;
        var now = DateTimeOffset.Now;
        _visibleAccounts = _accounts.Where(x =>
        {
            if (provider is not null && x.Provider != provider) return false;
            if (membership is not null && membership != "全部时长" && AccountPresentation.MembershipStatus(x, now) != membership) return false;
            if (term.Length == 0) return true;
            var number = x.PhoneId is Guid id && phoneMap.TryGetValue(id, out var phone) ? phone.Number + " " + phone.Label : "";
            return $"{x.Identifier} {x.Nickname} {x.Notes} {number}".Contains(term, StringComparison.OrdinalIgnoreCase);
        }).Select(x => new AccountRow(x, phoneMap, now)).ToList();
        AccountsGrid.ItemsSource = _visibleAccounts;
        AccountsGrid.SelectedItem = _visibleAccounts.FirstOrDefault(x => x.Record.Id == selectedId);
        AccountEmpty.Visibility = _visibleAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = _accounts.Count == 0 ? "从第一个账号开始" : "没有匹配的账号";
        EmptyDescription.Text = _accounts.Count == 0 ? "添加已有 QQ 或微博账号，记录实际到账时长和到期日期。" : "试试其他关键词，或调整渠道和时长筛选。";
        EmptyAddButton.Visibility = _accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountListSummary.Text = $"显示 {_visibleAccounts.Count} / {_accounts.Count} 个账号";
        UpdateDetails();
    }

    private void UpdateMetrics()
    {
        var now = DateTimeOffset.Now;
        TotalCount.Text = _accounts.Count.ToString();
        ActiveCount.Text = _accounts.Count(x => x.ExpiresAt > now).ToString();
        SoonCount.Text = _accounts.Count(x => x.ExpiresAt > now && x.ExpiresAt <= now.AddDays(3)).ToString();
        ExpiredSummary.Text = $"已到期 {_accounts.Count(x => x.ExpiresAt <= now)} 个";
        PendingCount.Text = _accounts.Count(x => x.CancellationState is CancellationStatus.Requested or CancellationStatus.Waiting or CancellationStatus.Unverified).ToString();
    }

    private void UpdateDetails()
    {
        var row = AccountsGrid.SelectedItem as AccountRow;
        DetailIdentifier.Text = row?.Identifier ?? "选择一个账号";
        DetailSubtitle.Text = row is null ? "在左侧查看和选择账号" : $"{row.Provider} · {row.Nickname} · {row.Phone}";
        DetailMembership.Text = row?.Membership ?? "未记录到期";
        DetailExpiry.Text = "到期时间：" + (row?.Expiry ?? "未记录");
        DetailCancellation.Text = "注销状态：" + (row?.Cancellation ?? "未记录");
        DetailWait.Text = "官方等待期限：" + (row?.Record.OfficialWaitUntil is DateTimeOffset date ? Display.Date(date) : "未知");
        ProviderEntryButton.Content = row?.Record.Provider == AccountProvider.Weibo ? "前往微博登录 / 注册" : "打开 QQ 注册";
        EditAccountButton.IsEnabled = CopyAccountButton.IsEnabled = NewRedemptionButton.IsEnabled = row is not null;
    }

    private void Navigate(string page)
    {
        _page = page;
        AccountsPage.Visibility = page == "accounts" ? Visibility.Visible : Visibility.Collapsed;
        PhonesPage.Visibility = page == "phones" ? Visibility.Visible : Visibility.Collapsed;
        RedemptionsPage.Visibility = page == "redemptions" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        var navs = new[] { (AccountsNav, "accounts"), (PhonesNav, "phones"), (RedemptionsNav, "redemptions"), (SettingsNav, "settings") };
        foreach (var (button, key) in navs)
        {
            button.Background = key == page ? new SolidColorBrush(Color.FromRgb(48, 58, 36)) : Brushes.Transparent;
            button.Foreground = new SolidColorBrush(key == page ? Color.FromRgb(185, 238, 84) : Color.FromRgb(160, 167, 151));
        }
        (PageTitle.Text, PageSubtitle.Text) = page switch
        {
            "phones" => ("手机号", "查看本人号码、账号关联与官方限制记录。"),
            "redemptions" => ("兑换记录", "每次兑换的口令、结果和实际到账时长。"),
            "settings" => ("设置与说明", "连接本机客户端，管理这台电脑上的记录。"),
            _ => ("账号总览", "账号、时长与到期进度，一处查看。")
        };
        ExportButton.Visibility = page == "settings" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Layout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (HeroRow is null) return;
        var compact = e.NewSize.Height < 740;
        HeroRow.Height = new GridLength(compact ? 80 : 154);
        WorkflowRow.Height = new GridLength(compact ? 68 : 88);
        SummaryRow.Height = new GridLength(compact ? 44 : 50);
        HeroIntro.Visibility = HeroDescription.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string message) => StatusText.Text = message;
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void RunUi(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            SetStatus("操作未完成：" + ex.Message);
            MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RunUi(() => Reload());
    private void AccountsNav_Click(object sender, RoutedEventArgs e) => Navigate("accounts");
    private void PhonesNav_Click(object sender, RoutedEventArgs e) => Navigate("phones");
    private void RedemptionsNav_Click(object sender, RoutedEventArgs e) => Navigate("redemptions");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => Navigate("settings");
    private void AccountFilters_Changed(object sender, RoutedEventArgs e) => ApplyAccountFilters();
    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateDetails(); }
}
