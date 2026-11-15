using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App.Dialogs;

public sealed class CredentialDialog : RecordDialog
{
    private readonly AccountRecord _account;
    private readonly AccountCredentialService _credentials;
    private readonly PasswordBox _password = new();
    private readonly TextBox _visible = new() { IsReadOnly = true, Visibility = Visibility.Collapsed };
    private readonly TextBlock _notice = new();

    public CredentialDialog(AccountRecord account, AccountCredentialService credentials)
        : base("账号密码", $"{AccountPresentation.ProviderLabel(account.Provider)} · {account.Identifier}", "加密保存密码")
    {
        Width = 650; Height = 590;
        _account = account; _credentials = credentials;
        _password.Password = credentials.ReadPassword(account.Id) ?? "";
        var panel = new StackPanel();
        panel.Children.Add(Field("密码", _password, "默认隐藏；可以填写或更新此账号的实际密码。"));
        AutomationProperties.SetName(_visible, "已显示的密码");
        panel.Children.Add(_visible);
        var reveal = new CheckBox { Content = "显示密码", Margin = new Thickness(0, 14, 0, 16) };
        reveal.Checked += (_, _) => { _visible.Text = _password.Password; _visible.Visibility = Visibility.Visible; };
        reveal.Unchecked += (_, _) => { _visible.Clear(); _visible.Visibility = Visibility.Collapsed; };
        _password.PasswordChanged += (_, _) => { if (reveal.IsChecked == true) _visible.Text = _password.Password; };
        panel.Children.Add(reveal);
        var copy = new Button { Content = "复制密码", HorizontalAlignment = HorizontalAlignment.Left };
        copy.Click += (_, _) =>
        {
            try { SecretClipboard.Copy(_password.Password); _notice.Text = "密码已复制；30 秒后会尝试清除未被替换的剪贴板内容。"; }
            catch (Exception ex) { _notice.Text = ex.Message; }
        };
        panel.Children.Add(copy);
        _notice.Style = (Style)FindResource("MutedText"); _notice.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(_notice);
        panel.Children.Add(new TextBlock { Text = "加密绑定当前 Windows 用户。更换用户或重装系统后，备份中的密码可能无法解密。CSV 导出不包含密码。", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 22, 0, 0) });
        FormHost.Content = Scroll(panel);
        Closed += (_, _) => { _password.Clear(); _visible.Clear(); };
    }

    protected override void SaveRecord() => _credentials.Save(_account, _password.Password);
}
