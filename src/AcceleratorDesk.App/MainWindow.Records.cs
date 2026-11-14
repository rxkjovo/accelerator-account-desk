using System.Windows;
using System.Windows.Input;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App;

public partial class MainWindow
{
    private void ShowAccountEditor(AccountRecord? account)
    {
        Guid savedId = account?.Id ?? Guid.Empty;
        var dialog = new AccountDialog(account, _phones, value => { _repository.SaveAccount(value); savedId = value.Id; }) { Owner = this };
        if (dialog.ShowDialog() == true) { Reload(savedId); SetStatus("账号记录已保存 · " + DateTime.Now.ToString("HH:mm")); }
    }
    private void NewAccount_Click(object sender, RoutedEventArgs e) => RunUi(() => ShowAccountEditor(null));
    private void EditAccount_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (AccountsGrid.SelectedItem is AccountRow row) ShowAccountEditor(row.Record);
    });
    private void AccountsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindRow(source) is not null) EditAccount_Click(sender, e);
    }
    private static System.Windows.Controls.DataGridRow? FindRow(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.DataGridRow row) return row;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }
    private void CopyAccount_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (AccountsGrid.SelectedItem is AccountRow row) { Clipboard.SetText(row.Identifier); SetStatus("账号已复制"); }
    });
    private void ShowPhoneEditor(PhoneRecord? phone)
    {
        var dialog = new PhoneDialog(phone, _repository.SavePhone) { Owner = this };
        if (dialog.ShowDialog() == true) { Reload(); SetStatus("手机号记录已保存 · " + DateTime.Now.ToString("HH:mm")); }
    }
    private void NewPhone_Click(object sender, RoutedEventArgs e) => RunUi(() => ShowPhoneEditor(null));
    private void EditPhone_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (PhonesGrid.SelectedItem is PhoneRow row) ShowPhoneEditor(row.Record);
        else SetStatus("请先选择一个手机号。");
    });
    private void PhonesGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindRow(source) is not null) EditPhone_Click(sender, e);
    }
    private void CopyPhone_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (PhonesGrid.SelectedItem is PhoneRow row) { Clipboard.SetText(row.Record.Number); SetStatus("手机号已复制"); }
        else SetStatus("请先选择一个手机号。");
    });
    private void ShowRedemptionEditor(RedemptionRecord? redemption)
    {
        if (_accounts.Count == 0) { SetStatus("先添加一个账号，再记录兑换结果。"); Navigate("accounts"); return; }
        var selectedAccount = (AccountsGrid.SelectedItem as AccountRow)?.Record.Id;
        Guid? savedAccount = redemption?.AccountId ?? selectedAccount;
        var dialog = new RedemptionDialog(redemption, _accounts, selectedAccount, value =>
        {
            _repository.SaveRedemption(value);
            savedAccount = value.AccountId;
        }) { Owner = this };
        if (dialog.ShowDialog() == true) { Reload(savedAccount); SetStatus("兑换结果已保存 · " + DateTime.Now.ToString("HH:mm")); }
    }
    private void NewRedemption_Click(object sender, RoutedEventArgs e) => RunUi(() => ShowRedemptionEditor(null));
    private void EditRedemption_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (RedemptionsGrid.SelectedItem is RedemptionRow row) ShowRedemptionEditor(row.Record);
        else SetStatus("请先选择一条兑换记录。");
    });
    private void RedemptionsGrid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindRow(source) is not null) EditRedemption_Click(sender, e);
    }
    private void CopyCode_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (RedemptionsGrid.SelectedItem is RedemptionRow row) { Clipboard.SetText(row.Record.Code); SetStatus("兑换口令已复制"); }
        else SetStatus("请先选择一条兑换记录。");
    });
}
