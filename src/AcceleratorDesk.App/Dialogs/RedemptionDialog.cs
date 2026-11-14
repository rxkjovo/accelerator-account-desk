using System.Windows.Controls;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App.Dialogs;

public sealed class RedemptionDialog : RecordDialog
{
    private readonly RedemptionRecord _record;
    private readonly Action<RedemptionRecord> _save;
    private readonly ComboBox _account, _outcome;
    private readonly TextBox _code, _redeemedAt, _duration, _expiry, _note;

    public RedemptionDialog(RedemptionRecord? record, IReadOnlyList<AccountRecord> accounts, Guid? selectedAccount, Action<RedemptionRecord> save)
        : base(record is null ? "记录兑换结果" : "编辑兑换记录", "在官方客户端兑换后，把实际结果保存在这里。")
    {
        _record = record is null ? new RedemptionRecord { AccountId = selectedAccount ?? accounts.First().Id } : record with { };
        _save = save;
        _account = Select(accounts.Select(x => new Choice<Guid>(x.Id, $"{AccountPresentation.ProviderLabel(x.Provider)} · {x.Identifier}  {x.Nickname}")), _record.AccountId);
        _account.IsEnabled = record is null;
        _outcome = Select(Enum.GetValues<RedemptionOutcome>().Select(x => new Choice<RedemptionOutcome>(x, AccountPresentation.OutcomeLabel(x))), _record.Outcome);
        _code = Input(_record.Code);
        _redeemedAt = Input(Display.DateInput(_record.RedeemedAt));
        _duration = Input(_record.GrantedDurationText);
        _expiry = Input(Display.DateInput(_record.ExpiresAt));
        _note = Input(_record.ResultNote, true);
        var body = new StackPanel();
        body.Children.Add(Field("兑换账号", _account));
        body.Children.Add(Field("兑换码 / 口令 *", _code));
        body.Children.Add(Field("官方返回结果", _outcome, "尚未确认时选择「待确认」。"));
        body.Children.Add(Field("兑换时间 *", _redeemedAt, "格式：yyyy-MM-dd HH:mm。"));
        body.Children.Add(Field("实际到账时长", _duration, "例如：7 天；不知道时留空。"));
        body.Children.Add(Field("实际到期时间", _expiry, "成功且填写时间时，更新该账号的到期记录；不会把时间往前调。"));
        body.Children.Add(Field("结果说明", _note));
        FormHost.Content = Scroll(body);
    }

    protected override void SaveRecord() => _save(_record with
    {
        AccountId = Selected<Guid>(_account), Code = _code.Text.Trim(), Outcome = Selected<RedemptionOutcome>(_outcome),
        RedeemedAt = Display.ParseDate(_redeemedAt.Text, "兑换时间") ?? throw new ArgumentException("请填写兑换时间。"),
        GrantedDurationText = _duration.Text.Trim(), ExpiresAt = Display.ParseDate(_expiry.Text, "到期时间"), ResultNote = _note.Text.Trim()
    });
}
