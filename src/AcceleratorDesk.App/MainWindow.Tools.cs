using System.IO;
using System.Windows;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using Microsoft.Win32;

namespace AcceleratorDesk.App;

public partial class MainWindow
{
    private void ProviderEntry_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if ((AccountsGrid.SelectedItem as AccountRow)?.Record.Provider == AccountProvider.Weibo)
            OpenClientWithHint("在迅雷登录窗口选择微博，再通过官方验证码或扫码入口完成登录 / 注册。");
        else ShowQqRegistration();
    });
    private void RegisterQq_Click(object sender, RoutedEventArgs e) => RunUi(ShowQqRegistration);
    private void ShowQqRegistration()
    {
        Directory.CreateDirectory(_dataDirectory);
        using var registrationLease = OpenRegistrationLease();
        var dialog = new Dialogs.QqRegistrationWindow(_repository, _dataDirectory) { Owner = this };
        dialog.ShowDialog();
        Reload(dialog.SavedAccountId);
        SetStatus(dialog.SavedAccountId is not null ? "QQ 账号、关联手机号和加密密码已保存。" : "注册向导已关闭；待确认资料会保存在本机加密草稿中。");
    }
    private FileStream OpenRegistrationLease()
    {
        try { return new FileStream(Path.Combine(_dataDirectory, "qq-registration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("当前数据目录已有一个注册向导，请先完成或关闭它，再开始新的注册。"); }
    }
    private void AccountPassword_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (AccountsGrid.SelectedItem is not AccountRow row) return;
        var dialog = new Dialogs.CredentialDialog(row.Record, new AccountCredentialService(_repository)) { Owner = this };
        if (dialog.ShowDialog() == true) SetStatus("账号密码已加密保存。");
    });
    private void OpenClient_Click(object sender, RoutedEventArgs e) => RunUi(() => OpenClientWithHint("在迅雷客户端完成 QQ / 微博授权和口令兑换，随后返回记录实际结果。"));
    private void OpenClientWithHint(string hint)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientExecutable))
        {
            Navigate("settings");
            SetStatus("先点击「选择客户端」，指定本机迅雷游戏加速器的 exe。");
            return;
        }
        OfficialLauncher.OpenClient(_settings.ClientExecutable);
        SetStatus(hint);
    }
    private void ChooseClient_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        var dialog = new OpenFileDialog { Title = "选择迅雷游戏加速器客户端", Filter = "Windows 程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var updated = new DeskSettings { ClientExecutable = Path.GetFullPath(dialog.FileName) };
        _settingsStore.Save(updated);
        _settings = updated;
        ClientPathBox.Text = updated.ClientExecutable;
        SetStatus("客户端路径已保存。");
    });
    private void OpenDataDirectory_Click(object sender, RoutedEventArgs e) => RunUi(() => OfficialLauncher.OpenDataDirectory(_dataDirectory));
    private void ExportAccounts_Click(object sender, RoutedEventArgs e) => RunUi(() => ExportAccounts(false));
    private void ExportAccounts(bool filtered)
    {
        var phoneMap = _phones.ToDictionary(x => x.Id);
        var records = filtered ? _visibleAccounts : _accounts.Select(x => new AccountRow(x, phoneMap, DateTimeOffset.Now)).ToList();
        var rows = new List<IEnumerable<string>> { new[] { "渠道", "账号", "备注", "手机号", "时长状态", "到期时间", "注销状态", "当前进度", "说明" } };
        rows.AddRange(records.Select(x => new[] { x.Provider, x.Identifier, x.Nickname, x.Phone, x.Membership, x.Expiry, x.Cancellation, x.Stage, x.Record.Notes }));
        SaveCsv("账号列表", rows);
    }
    private void ExportCurrent_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (_page == "phones")
        {
            var rows = new List<IEnumerable<string>> { new[] { "号码备注", "手机号（脱敏）", "关联账号数", "官方提示限制至", "官方说明" } };
            rows.AddRange(_phones.Select(x => new PhoneRow(x, _accounts)).Select(x => new[] { x.Label, x.Number, x.Accounts.ToString(), x.Restriction, x.Notice }));
            SaveCsv("手机号列表", rows);
        }
        else if (_page == "redemptions")
        {
            var accounts = _accounts.ToDictionary(x => x.Id);
            var rows = new List<IEnumerable<string>> { new[] { "账号", "渠道", "口令", "结果", "兑换时间", "到账时长", "实际到期", "说明" } };
            rows.AddRange(_redemptions.Select(x => new RedemptionRow(x, accounts)).Select(x => new[] { x.Account, x.Provider, x.Code, x.Outcome, x.Time, x.Duration, x.Expiry, x.Note }));
            SaveCsv("兑换记录", rows);
        }
        else ExportAccounts(true);
    });
    private void SaveCsv(string name, IEnumerable<IEnumerable<string>> rows)
    {
        var dialog = new SaveFileDialog { Title = "导出当前记录", Filter = "CSV 表格 (*.csv)|*.csv", FileName = $"{name}-{DateTime.Now:yyyyMMdd}.csv", AddExtension = true, DefaultExt = ".csv" };
        if (dialog.ShowDialog(this) != true) return;
        CsvExporter.Write(dialog.FileName, rows);
        SetStatus("已导出到：" + dialog.FileName);
    }
}
