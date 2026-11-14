using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

namespace AcceleratorDesk.App;

public sealed class AccountRow(AccountRecord account, IReadOnlyDictionary<Guid, PhoneRecord> phones, DateTimeOffset now)
{
    public AccountRecord Record { get; } = account;
    public string Provider => AccountPresentation.ProviderLabel(Record.Provider);
    public string Identifier => Record.Identifier;
    public string Nickname => string.IsNullOrWhiteSpace(Record.Nickname) ? "—" : Record.Nickname;
    public string Phone => Record.PhoneId is Guid id && phones.TryGetValue(id, out var phone) ? AccountPresentation.MaskPhone(phone.Number) : "未关联";
    public string Membership => AccountPresentation.MembershipStatus(Record, now);
    public string Expiry => Display.Date(Record.ExpiresAt);
    public string Cancellation => AccountPresentation.CancellationLabel(Record.CancellationState);
    public string Stage => AccountPresentation.StageLabel(Record.Stage);
}

public sealed class PhoneRow(PhoneRecord phone, IReadOnlyList<AccountRecord> accounts)
{
    public PhoneRecord Record { get; } = phone;
    public string Label => string.IsNullOrWhiteSpace(Record.Label) ? "未命名号码" : Record.Label;
    public string Number => AccountPresentation.MaskPhone(Record.Number);
    public int Accounts => accounts.Count(x => x.PhoneId == Record.Id);
    public string Restriction => Record.RestrictionUntil is null ? "期限未知" : Display.Date(Record.RestrictionUntil);
    public string Notice => string.IsNullOrWhiteSpace(Record.OfficialRestrictionNote) ? "未记录官方提示" : Record.OfficialRestrictionNote;
}

public sealed class RedemptionRow(RedemptionRecord redemption, IReadOnlyDictionary<Guid, AccountRecord> accounts)
{
    public RedemptionRecord Record { get; } = redemption;
    public string Account => accounts.TryGetValue(Record.AccountId, out var account) ? account.Identifier : "账号记录不可用";
    public string Provider => accounts.TryGetValue(Record.AccountId, out var account) ? AccountPresentation.ProviderLabel(account.Provider) : "—";
    public string Code => Record.Code;
    public string Outcome => AccountPresentation.OutcomeLabel(Record.Outcome);
    public string Time => Display.Date(Record.RedeemedAt);
    public string Duration => string.IsNullOrWhiteSpace(Record.GrantedDurationText) ? "未记录" : Record.GrantedDurationText;
    public string Expiry => Display.Date(Record.ExpiresAt);
    public string Note => Record.ResultNote;
}
