using AcceleratorDesk.Core;
using Microsoft.Data.Sqlite;

var cases = new (string Name, Action Test)[]
{
    ("fresh persistent database is empty", () => WithRepository((repo, _) =>
    {
        Equal(0, repo.GetAccounts().Count); Equal(0, repo.GetPhones().Count); Equal(0, repo.GetRedemptions().Count);
    })),
    ("QQ and Weibo, phone links, unknown dates and Unicode survive reopen", () => WithRepository((repo, path) =>
    {
        var phone = new PhoneRecord { Number = "+86 13800138000", OfficialRestrictionNote = "官方未说明", Label = "常用" };
        repo.SavePhone(phone);
        var qq = new AccountRecord { Identifier = "123456", PhoneId = phone.Id, Notes = "引号 ' \"；中文 😀\n第二行", Nickname = "昵称", OfficialNotice = "官方通知" };
        var weibo = new AccountRecord { Provider = AccountProvider.Weibo, Identifier = "123456" };
        repo.SaveAccount(qq); repo.SaveAccount(weibo);
        var reopened = new DeskRepository(path); reopened.Initialize();
        Equal(qq, reopened.GetAccounts().Single(x => x.Id == qq.Id));
        Equal(weibo, reopened.GetAccounts().Single(x => x.Id == weibo.Id));
        Equal(phone, reopened.GetPhones().Single()); Equal<DateTimeOffset?>(null, reopened.GetPhones().Single().RestrictionUntil);
    })),
    ("duplicate provider identity is rejected and cross-provider identity allowed", () => WithRepository((repo, _) =>
    {
        repo.SaveAccount(new AccountRecord { Identifier = "alice" });
        Throws(() => repo.SaveAccount(new AccountRecord { Identifier = " ALICE " }));
        repo.SaveAccount(new AccountRecord { Identifier = "alice", Provider = AccountProvider.Weibo }); Equal(2, repo.GetAccounts().Count);
    })),
    ("account and phone edits upsert without adding rows", () => WithRepository((repo, _) =>
    {
        var phone = new PhoneRecord { Number = "13800138000" }; repo.SavePhone(phone); repo.SavePhone(phone with { Label = "更新" });
        var account = NewAccount(repo); repo.SaveAccount(account with { Nickname = "新昵称" });
        Equal(1, repo.GetPhones().Count); Equal("更新", repo.GetPhones().Single().Label);
        Equal(1, repo.GetAccounts().Count); Equal("新昵称", repo.GetAccounts().Single().Nickname);
    })),
    ("membership boundaries use the supplied clock and preserve unknown", () =>
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8));
        Equal("未记录到期", AccountPresentation.MembershipStatus(new(), now));
        Equal("已到期", AccountPresentation.MembershipStatus(new() { ExpiresAt = now }, now));
        Equal("已到期", AccountPresentation.MembershipStatus(new() { ExpiresAt = now.AddTicks(-1) }, now));
        Equal("即将到期", AccountPresentation.MembershipStatus(new() { ExpiresAt = now.AddDays(3) }, now));
        Equal("使用中", AccountPresentation.MembershipStatus(new() { ExpiresAt = now.AddDays(3).AddTicks(1) }, now));
    }),
    ("Chinese enum labels and privacy masking", () =>
    {
        Equal("QQ", AccountPresentation.ProviderLabel(AccountProvider.QQ)); Equal("微博", AccountPresentation.ProviderLabel(AccountProvider.Weibo));
        Equal("已注册", AccountPresentation.StageLabel(AccountStage.Registered)); Equal("已授权", AccountPresentation.StageLabel(AccountStage.Authorized));
        Equal("已兑换", AccountPresentation.StageLabel(AccountStage.Redeemed)); Equal("成功", AccountPresentation.OutcomeLabel(RedemptionOutcome.Succeeded));
        foreach (var value in Enum.GetValues<CancellationStatus>()) Check(!string.IsNullOrWhiteSpace(AccountPresentation.CancellationLabel(value)), "empty label");
        Equal("138****8000", AccountPresentation.MaskPhone("13800138000")); Equal("", AccountPresentation.MaskPhone(""));
        Check(!AccountPresentation.MaskPhone("12345").Contains("12345", StringComparison.Ordinal), "short phone leaked");
    }),
    ("completed cancellation remains queryable and requires date", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo); var requested = DateTimeOffset.UtcNow.AddDays(-3);
        Throws(() => repo.SaveAccount(account with { CancellationState = CancellationStatus.Completed }));
        Throws(() => repo.SaveAccount(account with { CancellationRequestedAt = requested, CancellationCompletedAt = requested.AddTicks(-1) }));
        Throws(() => repo.SaveAccount(account with { CancellationRequestedAt = requested, OfficialWaitUntil = requested.AddDays(-1) }));
        repo.SaveAccount(account with { CancellationState = CancellationStatus.Waiting, CancellationRequestedAt = requested });
        Equal<DateTimeOffset?>(null, repo.GetAccounts().Single().OfficialWaitUntil);
        var completed = account with { CancellationState = CancellationStatus.Completed, CancellationRequestedAt = requested, CancellationCompletedAt = requested.AddDays(2) };
        repo.SaveAccount(completed); Equal(completed, repo.GetAccounts().Single());
    })),
    ("pending and failed redemption preserve account state and expiry", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo); var expiry = DateTimeOffset.UtcNow.AddDays(20);
        foreach (var outcome in new[] { RedemptionOutcome.Pending, RedemptionOutcome.Failed })
            repo.SaveRedemption(new() { AccountId = account.Id, Code = "测试码", Outcome = outcome, ExpiresAt = expiry });
        Equal(account, repo.GetAccounts().Single()); Equal(2, repo.GetRedemptions().Count);
    })),
    ("success advances stage and only uses supplied expiry", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo); var expiry = DateTimeOffset.UtcNow.AddDays(5);
        var record = new RedemptionRecord { AccountId = account.Id, Code = "成功码", Outcome = RedemptionOutcome.Succeeded, GrantedDurationText = "声称一个月", ExpiresAt = expiry, ResultNote = "官方成功" };
        repo.SaveRedemption(record); Equal(AccountStage.Redeemed, repo.GetAccounts().Single().Stage); Equal<DateTimeOffset?>(expiry, repo.GetAccounts().Single().ExpiresAt);
        Equal(record, repo.GetRedemptions(account.Id).Single());
        repo.SaveRedemption(new() { AccountId = account.Id, Code = "未知期限", Outcome = RedemptionOutcome.Succeeded, GrantedDurationText = "999 天" });
        Equal<DateTimeOffset?>(expiry, repo.GetAccounts().Single().ExpiresAt);
        var unknown = NewAccount(repo, "未知到期");
        repo.SaveRedemption(new() { AccountId = unknown.Id, Code = "无到期", Outcome = RedemptionOutcome.Succeeded, GrantedDurationText = "1 天" });
        Equal<DateTimeOffset?>(null, repo.GetAccounts().Single(x => x.Id == unknown.Id).ExpiresAt);
    })),
    ("successful edits never replay expiry; pending to success applies once", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo); var oldExpiry = DateTimeOffset.UtcNow.AddDays(5); var laterExpiry = oldExpiry.AddDays(10);
        var old = new RedemptionRecord { AccountId = account.Id, Code = "旧码", Outcome = RedemptionOutcome.Succeeded, ExpiresAt = oldExpiry };
        repo.SaveRedemption(old);
        var pending = new RedemptionRecord { AccountId = account.Id, Code = "新码" }; repo.SaveRedemption(pending);
        repo.SaveRedemption(pending with { Outcome = RedemptionOutcome.Succeeded, ExpiresAt = laterExpiry });
        repo.SaveRedemption(old with { ResultNote = "编辑历史", ExpiresAt = laterExpiry.AddDays(100) });
        Equal<DateTimeOffset?>(laterExpiry, repo.GetAccounts().Single().ExpiresAt); Equal(2, repo.GetRedemptions().Count);
        repo.SaveRedemption(old with { Outcome = RedemptionOutcome.Failed });
        repo.SaveRedemption(old with { Outcome = RedemptionOutcome.Succeeded, ExpiresAt = laterExpiry.AddDays(100) });
        Equal<DateTimeOffset?>(laterExpiry, repo.GetAccounts().Single().ExpiresAt);
    })),
    ("new successes never reduce expiry and accept explicitly past expiry", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo); var later = DateTimeOffset.UtcNow.AddDays(30);
        repo.SaveAccount(account with { ExpiresAt = later });
        repo.SaveRedemption(new() { AccountId = account.Id, Code = "旧到期", Outcome = RedemptionOutcome.Succeeded, ExpiresAt = later.AddDays(-20) });
        Equal<DateTimeOffset?>(later, repo.GetAccounts().Single().ExpiresAt);
        var historical = NewAccount(repo, "历史账号"); var past = DateTimeOffset.UtcNow.AddYears(-1);
        repo.SaveRedemption(new() { AccountId = historical.Id, Code = "历史码", Outcome = RedemptionOutcome.Succeeded, ExpiresAt = past, RedeemedAt = past.AddDays(-1) });
        Equal<DateTimeOffset?>(past, repo.GetAccounts().Single(x => x.Id == historical.Id).ExpiresAt);
    })),
    ("invalid foreign keys preserve all prior rows", () => WithRepository((repo, _) =>
    {
        var account = NewAccount(repo);
        Throws(() => repo.SaveAccount(account with { PhoneId = Guid.NewGuid(), Notes = "不能保存" })); Equal(account, repo.GetAccounts().Single());
        Throws(() => repo.SaveRedemption(new() { AccountId = Guid.NewGuid(), Code = "无效关联", Outcome = RedemptionOutcome.Succeeded }));
        Equal(0, repo.GetRedemptions().Count); Equal(account, repo.GetAccounts().Single());
        var valid = new RedemptionRecord { AccountId = account.Id, Code = "有效" }; repo.SaveRedemption(valid);
        Throws(() => repo.SaveRedemption(valid with { AccountId = Guid.NewGuid(), Outcome = RedemptionOutcome.Succeeded }));
        Equal(valid, repo.GetRedemptions().Single()); Equal(account, repo.GetAccounts().Single());
    })),
    ("SQLite failure rolls redemption and account mutation back together", () => WithRepository((repo, path) =>
    {
        var account = NewAccount(repo);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER prevent_account_update BEFORE UPDATE ON accounts BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;";
            command.ExecuteNonQuery();
        }
        Throws(() => repo.SaveRedemption(new() { AccountId = account.Id, Code = "事务测试", Outcome = RedemptionOutcome.Succeeded, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }));
        Equal(0, repo.GetRedemptions().Count); Equal(account, repo.GetAccounts().Single());
    })),
    ("failed pending-to-success transaction can retry after reopen", () => WithRepository((repo, path) =>
    {
        var account = NewAccount(repo); var record = new RedemptionRecord { AccountId = account.Id, Code = "O'Brien 中文; DROP TABLE accounts;--", ResultNote = "原记录" };
        repo.SaveRedemption(record);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER prevent_account_update BEFORE UPDATE ON accounts BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;";
            command.ExecuteNonQuery();
        }
        var success = record with { Outcome = RedemptionOutcome.Succeeded, ExpiresAt = DateTimeOffset.UtcNow.AddDays(12), ResultNote = "官方成功" };
        Throws(() => repo.SaveRedemption(success)); Equal(record, repo.GetRedemptions().Single()); Equal(account, repo.GetAccounts().Single());
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER prevent_account_update"; command.ExecuteNonQuery();
        }
        var reopened = new DeskRepository(path); reopened.Initialize(); reopened.SaveRedemption(success);
        Equal(success, reopened.GetRedemptions().Single()); Equal(success.ExpiresAt, reopened.GetAccounts().Single().ExpiresAt);
        reopened.SaveRedemption(success with { Outcome = RedemptionOutcome.Failed });
        var secondReopen = new DeskRepository(path); secondReopen.Initialize();
        secondReopen.SaveRedemption(success with { ExpiresAt = success.ExpiresAt!.Value.AddYears(1) });
        Equal(success.ExpiresAt, secondReopen.GetAccounts().Single().ExpiresAt);
    })),
    ("concurrent repository writers retain the greatest supplied expiry", () => WithRepository((repo, path) =>
    {
        var account = NewAccount(repo); var start = DateTimeOffset.UtcNow;
        Task.WaitAll(Enumerable.Range(1, 8).Select(days => Task.Run(() =>
        {
            var other = new DeskRepository(path);
            other.SaveRedemption(new() { AccountId = account.Id, Code = $"并发-{days}", Outcome = RedemptionOutcome.Succeeded, ExpiresAt = start.AddDays(days) });
        })).ToArray());
        Equal(8, repo.GetRedemptions().Count); Equal<DateTimeOffset?>(start.AddDays(8), repo.GetAccounts().Single().ExpiresAt);
    })),
    ("invalid inputs give actionable Chinese validation errors", () => WithRepository((repo, _) =>
    {
        Throws(() => repo.SaveAccount(new() { Identifier = " " }));
        Throws(() => repo.SaveAccount(new() { Id = Guid.Empty, Identifier = "账号" }));
        Throws(() => repo.SaveAccount(new() { Provider = (AccountProvider)42, Identifier = "账号" }));
        foreach (var invalid in new[] { "", "123", "138abc38000", "++8613800138000", "1234567890123456", "123(45678", "123)45678", "123((45))678" }) Throws(() => repo.SavePhone(new() { Number = invalid }));
        var account = NewAccount(repo); Throws(() => repo.SaveRedemption(new() { AccountId = account.Id, Code = " " }));
        Throws(() => repo.SaveRedemption(new() { AccountId = account.Id, Code = "码", Outcome = (RedemptionOutcome)42 }));
    }))
};

var failures = 0;
foreach (var test in cases)
{
    try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"RESULT {cases.Length - failures}/{cases.Length} passed; {failures} failed");
return failures == 0 ? 0 : 1;

static void WithRepository(Action<DeskRepository, string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDesk.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    try { var path = Path.Combine(directory, "records.sqlite3"); var repo = new DeskRepository(path); repo.Initialize(); test(repo, path); }
    finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}
static AccountRecord NewAccount(DeskRepository repo, string identifier = "account") { var account = new AccountRecord { Identifier = identifier }; repo.SaveAccount(account); return account; }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws(Action action)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        Check(ex.Message.Any(c => c is >= '\u4e00' and <= '\u9fff'), "validation message must be actionable Chinese"); return;
    }
    throw new Exception("Expected a Chinese ArgumentException or InvalidOperationException");
}
