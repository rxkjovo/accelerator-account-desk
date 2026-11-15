using System.IO;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

internal static class SessionChecks
{
    public static void Run()
    {
        var passed = 0;
        var failed = 0;
        void Check(string name, Action<DeskRepository, string, PhoneRecord> body)
        {
            var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDeskSession-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new DeskRepository(Path.Combine(directory, "records.db")); repository.Initialize();
                var phone = new PhoneRecord { Number = "13800138000", Label = "独立测试号码" }; repository.SavePhone(phone);
                body(repository, directory, phone); passed++; Console.WriteLine("PASS SESSION " + name);
            }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL SESSION " + name + ": " + ex.Message); }
        }
        Check("preparing registration creates recoverable encrypted draft without account", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory);
            var draft = session.Prepare(phone);
            Equal(0, repository.GetAccounts().Count); Equal(phone.Id, draft.PhoneId);
            var restored = new QqRegistrationSession(repository, directory);
            Equal(draft.Password, restored.Draft!.Password); Equal(draft.AccountId, restored.Draft.AccountId);
        });
        Check("prepared phone cannot be swapped under an existing result", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone);
            var other = new PhoneRecord { Number = "13900139000" }; repository.SavePhone(other);
            Reject(() => session.Prepare(other)); Equal(phone.Id, session.Draft!.PhoneId);
        });
        Check("page success text before explicit submission never creates an account", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone);
            Equal(false, session.TrySaveOfficialResult(QqRegistrationPolicy.RegistrationUrl, "注册成功\nQQ号码：9081762354"));
            Equal(0, repository.GetAccounts().Count);
        });
        Check("submitted official success saves phone and password once without invented membership", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); var draft = session.Prepare(phone); session.MarkSubmitted();
            Equal(true, session.TrySaveOfficialResult(QqRegistrationPolicy.RegistrationUrl, "注册成功\nQQ号码：9081762354"));
            Equal(true, session.TrySaveOfficialResult(QqRegistrationPolicy.RegistrationUrl, "注册成功\nQQ号码：9081762354"));
            var account = repository.GetAccounts().Single(); Equal(phone.Id, account.PhoneId); Equal("9081762354", account.Identifier); Equal(AccountStage.Registered, account.Stage); Equal<DateTimeOffset?>(null, account.ExpiresAt);
            Equal(draft.Password, new AccountCredentialService(repository).ReadPassword(account.Id));
            Equal<QqRegistrationDraft?>(null, new QqRegistrationDraftStore(directory).Load());
        });
        Check("unrelated or uncertain pages retain draft and no successful account", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone); session.MarkSubmitted();
            Equal(false, session.TrySaveOfficialResult("https://evil.test/", "注册成功\nQQ号：9081762354"));
            Equal(false, session.TrySaveOfficialResult(QqRegistrationPolicy.RegistrationUrl, "验证码错误"));
            Equal(0, repository.GetAccounts().Count); Equal(true, new QqRegistrationSession(repository, directory).Draft!.SubmissionAttempted);
        });
        Check("manual fallback requires user confirmation and valid QQ", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone);
            Reject(() => session.ConfirmManual("9081762354", false)); Reject(() => session.ConfirmManual("not-a-qq", true)); Reject(() => session.ConfirmManual("13800138000", true));
            Equal(0, repository.GetAccounts().Count); session.ConfirmManual("9081762354", true); Equal(1, repository.GetAccounts().Count);
        });
        Check("duplicate QQ save failure retains draft and does not overwrite another account password", (repository, directory, phone) =>
        {
            var existing = new AccountRecord { Identifier = "9081762354" }; var credentials = new AccountCredentialService(repository); credentials.Save(existing, "ExistingFixture2");
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone);
            Reject(() => session.ConfirmManual("9081762354", true));
            Equal("ExistingFixture2", credentials.ReadPassword(existing.Id)); Equal(1, repository.GetAccounts().Count); Equal(true, new QqRegistrationDraftStore(directory).Load() is not null);
        });
        Check("known unsent rejection unlocks code correction without losing generated password", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); var draft = session.Prepare(phone); session.MarkSubmitted();
            session.ReleaseUnsentSubmission(); Equal(false, session.Draft!.SubmissionAttempted); Equal(draft.Password, session.Draft.Password);
            Equal(false, new QqRegistrationSession(repository, directory).Draft!.SubmissionAttempted);
        });
        Check("only explicit official code failure permits another user submission", (repository, directory, phone) =>
        {
            var session = new QqRegistrationSession(repository, directory); session.Prepare(phone); session.MarkSubmitted();
            Equal(false, session.AllowCorrectedCodeRetry("https://evil.test/", "验证码错误")); Equal(true, session.Draft!.SubmissionAttempted);
            Equal(false, session.AllowCorrectedCodeRetry(QqRegistrationPolicy.RegistrationUrl, "正在注册")); Equal(true, session.Draft!.SubmissionAttempted);
            Equal(true, session.AllowCorrectedCodeRetry(QqRegistrationPolicy.RegistrationUrl, "验证码错误，请重新输入")); Equal(false, session.Draft!.SubmissionAttempted);
        });
        Console.WriteLine($"RESULT SESSION {passed}/{passed + failed} passed; {failed} failed");
        if (failed != 0) throw new Exception("Registration session checks failed.");
    }

    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid state was accepted."); }
}
