using System.Windows.Controls;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App.Dialogs;

public sealed class PhoneDialog : RecordDialog
{
    private readonly PhoneRecord _record;
    private readonly Action<PhoneRecord> _save;
    private readonly TextBox _number, _label, _until, _notice;

    public PhoneDialog(PhoneRecord? record, Action<PhoneRecord> save)
        : base(record is null ? "添加手机号" : "编辑手机号", "保存本人号码的账号关联和官方提示，方便查询。")
    {
        Height = 640;
        _record = record is null ? new PhoneRecord() : record with { };
        _save = save;
        _number = Input(_record.Number);
        _label = Input(_record.Label);
        _until = Input(Display.DateInput(_record.RestrictionUntil));
        _notice = Input(_record.OfficialRestrictionNote, true);
        var body = new StackPanel();
        body.Children.Add(Field("手机号 *", _number, "填写本人持有的号码，可包含国家区号。"));
        body.Children.Add(Field("号码备注", _label, "例如：常用号码、备用号码。"));
        body.Children.Add(Field("官方提示限制至", _until, "格式：yyyy-MM-dd HH:mm；期限未知时留空。"));
        body.Children.Add(Field("官方限制说明", _notice));
        FormHost.Content = Scroll(body);
    }

    protected override void SaveRecord() => _save(_record with
    {
        Number = _number.Text.Trim(), Label = _label.Text.Trim(),
        RestrictionUntil = Display.ParseDate(_until.Text, "官方限制期限"), OfficialRestrictionNote = _notice.Text.Trim()
    });
}
