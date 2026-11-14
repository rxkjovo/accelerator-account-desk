using System.Windows.Controls;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App.Dialogs;

public sealed class AccountDialog : RecordDialog
{
    private readonly AccountRecord _record;
    private readonly Action<AccountRecord> _save;
    private readonly ComboBox _provider, _phone, _stage, _cancellation;
    private readonly TextBox _identifier, _nickname, _expiry, _notes, _requested, _completed, _waitUntil, _notice;

    public AccountDialog(AccountRecord? record, IReadOnlyList<PhoneRecord> phones, Action<AccountRecord> save)
        : base(record is null ? "新增账号" : "编辑账号", "记录你自己的账号信息，时长和注销结果以官方页面为准。")
    {
        _record = record is null ? new AccountRecord() : record with { };
        _save = save;
        _provider = Select(Enum.GetValues<AccountProvider>().Select(x => new Choice<AccountProvider>(x, AccountPresentation.ProviderLabel(x))), _record.Provider);
        _identifier = Input(_record.Identifier);
        _nickname = Input(_record.Nickname);
        _phone = Select(new[] { new Choice<Guid?>(null, "暂不关联手机号") }.Concat(phones.Select(x => new Choice<Guid?>(x.Id, $"{x.Label}  {AccountPresentation.MaskPhone(x.Number)}"))), _record.PhoneId);
        _stage = Select(Enum.GetValues<AccountStage>().Select(x => new Choice<AccountStage>(x, AccountPresentation.StageLabel(x))), _record.Stage);
        _expiry = Input(Display.DateInput(_record.ExpiresAt));
        _notes = Input(_record.Notes, true);
        _cancellation = Select(Enum.GetValues<CancellationStatus>().Select(x => new Choice<CancellationStatus>(x, AccountPresentation.CancellationLabel(x))), _record.CancellationState);
        _requested = Input(Display.DateInput(_record.CancellationRequestedAt));
        _completed = Input(Display.DateInput(_record.CancellationCompletedAt));
        _waitUntil = Input(Display.DateInput(_record.OfficialWaitUntil));
        _notice = Input(_record.OfficialNotice, true);
        var basics = new StackPanel();
        basics.Children.Add(Field("登录渠道", _provider));
        basics.Children.Add(Field("QQ 号 / 微博账号标识 *", _identifier));
        basics.Children.Add(Field("账号备注", _nickname));
        basics.Children.Add(Field("关联手机号", _phone, "可先在「手机号」页面添加号码。"));
        basics.Children.Add(Field("当前进度", _stage));
        basics.Children.Add(Field("补充说明", _notes));
        var membership = new StackPanel();
        membership.Children.Add(Field("实际到期时间", _expiry, "格式：yyyy-MM-dd HH:mm；不知道时留空。"));
        membership.Children.Add(new TextBlock { Text = "这里只记录你实际看到的会员到期时间。未填写时显示「未记录到期」，不会自动增加赠送天数。", Style = (System.Windows.Style)FindResource("MutedText") });
        membership.Children.Add(new TextBlock { Text = $"本地建档时间：{Display.Date(_record.CreatedAt)}", Style = (System.Windows.Style)FindResource("MutedText"), Margin = new System.Windows.Thickness(0, 20, 0, 0) });
        var cancellation = new StackPanel();
        cancellation.Children.Add(Field("平台账号注销状态", _cancellation));
        cancellation.Children.Add(Field("注销申请时间", _requested, "可选，格式：yyyy-MM-dd HH:mm。"));
        cancellation.Children.Add(Field("官方提示等待至", _waitUntil, "没有明确期限时留空，不据此推定手机号可再次注册。"));
        cancellation.Children.Add(Field("官方确认注销完成时间", _completed, "选择「已完成」时，需要填写官方确认的完成时间。"));
        cancellation.Children.Add(Field("官方提示 / 结果来源", _notice));
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "基本资料", Content = Scroll(basics) });
        tabs.Items.Add(new TabItem { Header = "会员时长", Content = Scroll(membership) });
        tabs.Items.Add(new TabItem { Header = "注销记录", Content = Scroll(cancellation) });
        FormHost.Content = tabs;
    }

    protected override void SaveRecord()
    {
        var updated = _record with
        {
            Provider = Selected<AccountProvider>(_provider), Identifier = _identifier.Text.Trim(), Nickname = _nickname.Text.Trim(),
            PhoneId = Selected<Guid?>(_phone), Stage = Selected<AccountStage>(_stage),
            ExpiresAt = Display.ParseDate(_expiry.Text, "到期时间"), Notes = _notes.Text.Trim(),
            CancellationState = Selected<CancellationStatus>(_cancellation),
            CancellationRequestedAt = Display.ParseDate(_requested.Text, "申请时间"),
            CancellationCompletedAt = Display.ParseDate(_completed.Text, "完成时间"),
            OfficialWaitUntil = Display.ParseDate(_waitUntil.Text, "官方等待期限"), OfficialNotice = _notice.Text.Trim()
        };
        _save(updated);
    }
}
