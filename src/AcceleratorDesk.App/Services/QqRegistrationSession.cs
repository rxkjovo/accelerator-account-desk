using AcceleratorDesk.Core;
using System.Text.RegularExpressions;

namespace AcceleratorDesk.App.Services;

public sealed class QqRegistrationSession
{
    private readonly DeskRepository _repository;
    private readonly AccountCredentialService _credentials;
    private readonly QqRegistrationDraftStore _drafts;

    public QqRegistrationSession(DeskRepository repository, string dataDirectory)
    {
        _repository = repository;
        _credentials = new AccountCredentialService(repository);
        _drafts = new QqRegistrationDraftStore(dataDirectory);
        Draft = _drafts.Load();
    }
    public QqRegistrationDraft? Draft { get; private set; }
    public Guid? SavedAccountId { get; private set; }
    public string CleanupWarning { get; private set; } = "";
    public QqRegistrationDraft Prepare(PhoneRecord phone)
    {
        if (SavedAccountId is not null) throw new InvalidOperationException("本次账号已经保存，请关闭向导后开始下一次注册。");
        var number = QqRegistrationPolicy.NormalizePhone(phone.Number);
        if (!_repository.GetPhones().Any(x => x.Id == phone.Id && QqRegistrationPolicy.NormalizePhone(x.Number) == number))
            throw new ArgumentException("所选手机号已不存在或已更改，请重新选择。");
        if (Draft is not null)
        {
            if (Draft.PhoneId != phone.Id || Draft.PhoneNumber != number)
                throw new InvalidOperationException("当前有待确认的注册，请先核对并保存当前结果；不能在同一草稿中更换手机号。");
            return Draft;
        }
        var generated = QqRegistrationPolicy.CreateCredentials();
        var draft = new QqRegistrationDraft { PhoneId = phone.Id, PhoneNumber = number, Nickname = generated.Nickname, Password = generated.Password };
        _drafts.Save(draft);
        return Draft = draft;
    }

    public void MarkSubmitted()
    {
        var draft = Draft ?? throw new InvalidOperationException("请先选择手机号并开始注册。");
        var updated = draft with { SubmissionAttempted = true };
        _drafts.Save(updated); Draft = updated;
    }

    public void ReleaseUnsentSubmission()
    {
        if (Draft is null || SavedAccountId is not null) return;
        var updated = Draft with { SubmissionAttempted = false };
        _drafts.Save(updated); Draft = updated;
    }
    public bool AllowCorrectedCodeRetry(string source, string visibleText)
    {
        if (Draft is not { SubmissionAttempted: true } || SavedAccountId is not null || !QqRegistrationPolicy.IsRegistrationPage(source)) return false;
        if (!Regex.IsMatch(visibleText, "验证码错误|验证码不正确|验证码已过期|验证码已失效")) return false;
        ReleaseUnsentSubmission(); return true;
    }

    public bool TrySaveOfficialResult(string source, string visibleText)
    {
        if (SavedAccountId is not null) return true;
        if (Draft is not { SubmissionAttempted: true } draft) return false;
        var qq = QqRegistrationPolicy.ReadSuccessfulQq(source, visibleText, draft.PhoneNumber);
        if (qq is null) return false;
        SaveConfirmed(qq); return true;
    }

    public Guid ConfirmManual(string qqNumber, bool userConfirmed)
    {
        if (!userConfirmed) throw new ArgumentException("请先确认已在 QQ 官方页面看到注册成功及所填 QQ 号。");
        return SaveConfirmed(qqNumber.Trim());
    }

    private Guid SaveConfirmed(string qqNumber)
    {
        if (SavedAccountId is Guid saved) return saved;
        var draft = Draft ?? throw new InvalidOperationException("请先选择手机号并开始注册，以便关联本次注册资料。");
        if (!Regex.IsMatch(qqNumber, @"^[1-9][0-9]{4,11}$") || qqNumber == draft.PhoneNumber)
            throw new ArgumentException("请填写官方成功页面显示的 QQ 号，不能填写手机号。");
        var existing = _repository.GetAccounts().FirstOrDefault(x => x.Id == draft.AccountId);
        var account = existing ?? new AccountRecord
        {
            Id = draft.AccountId, Provider = AccountProvider.QQ, Identifier = qqNumber, Nickname = draft.Nickname,
            PhoneId = draft.PhoneId, Stage = AccountStage.Registered, CreatedAt = draft.CreatedAt
        };
        if (existing is not null && (existing.Provider != AccountProvider.QQ || existing.Identifier != qqNumber))
            throw new InvalidOperationException("草稿账号与已保存记录不一致，请核对已有账号。");
        _credentials.Save(account, draft.Password);
        SavedAccountId = account.Id;
        try { _drafts.Clear(); }
        catch (InvalidOperationException ex) { CleanupWarning = ex.Message; }
        return account.Id;
    }
}
