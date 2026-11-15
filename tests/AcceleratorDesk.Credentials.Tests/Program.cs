using System.IO;
using System.Security.Cryptography;
using System.Text;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using Microsoft.Data.Sqlite;

var cases = new (string Name, Action Test)[]
{
    ("newer database versions reject without downgrading the version or touching records", () => WithRepository((repo, path) =>
    {
        var account = Account(); repo.SaveAccount(account);
        Execute(path, "PRAGMA user_version = 3");
        Throws<InvalidOperationException>(() => repo.Initialize());
        Equal(3L, Scalar(path, "PRAGMA user_version")); Equal(account, repo.GetAccounts().Single());
    })),
    ("original v1 schema upgrades idempotently without losing records or redemption state", () => WithDatabase((path) =>
    {
        Execute(path, LegacySchema);
        var legacy = new DeskRepository(path);
        var phone = new PhoneRecord { Number = "13800138000", Label = "旧数据 😀" };
        legacy.SavePhone(phone);
        var account = new AccountRecord { Identifier = "600001", PhoneId = phone.Id, Notes = "原有备注" };
        legacy.SaveAccount(account);
        var redemption = new RedemptionRecord { AccountId = account.Id, Code = "历史码", Outcome = RedemptionOutcome.Succeeded };
        legacy.SaveRedemption(redemption);
        var originalAccount = legacy.GetAccounts().Single();
        Equal(1L, Scalar(path, "PRAGMA user_version"));
        var migrated = new DeskRepository(path); migrated.Initialize(); migrated.Initialize();
        Equal(2L, Scalar(path, "PRAGMA user_version"));
        Equal(phone, migrated.GetPhones().Single()); Equal(originalAccount, migrated.GetAccounts().Single());
        Equal(redemption, migrated.GetRedemptions().Single());
        Equal(1L, Scalar(path, "SELECT account_effect_applied FROM redemptions"));
        Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        var service = NewService(migrated); Save(service, originalAccount, "迁移后密码");
        var reopened = new DeskRepository(path); reopened.Initialize();
        Equal("迁移后密码", Read(NewService(reopened), account.Id));
        Equal(1, reopened.GetAccounts().Count); Equal(redemption, reopened.GetRedemptions().Single());
    })),
    ("fresh databases and accounts without passwords have no secret", () => WithRepository((repo, path) =>
    {
        var service = NewService(repo); Equal<string?>(null, Read(service, Guid.NewGuid()));
        var account = Account(); Save(service, account, null);
        Equal(account, repo.GetAccounts().Single()); Equal<string?>(null, Read(service, account.Id));
        Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
    })),
    ("Unicode and surrounding spaces survive restart exactly with CurrentUser account entropy", () => WithRepository((repo, path) =>
    {
        var account = Account(); const string password = "  密码'\"😀\tAa9!\n尾部空格  ";
        Save(NewService(repo), account, password);
        SqliteConnection.ClearAllPools();
        var reopened = new DeskRepository(path); reopened.Initialize();
        Equal(password, Read(NewService(reopened), account.Id));
        var blob = Protected(reopened, account.Id)!;
        var plaintext = ProtectedData.Unprotect(blob, account.Id.ToByteArray(), DataProtectionScope.CurrentUser);
        try { Equal(password, Encoding.UTF8.GetString(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Equal(account, reopened.GetAccounts().Single());
    })),
    ("null password and ordinary repository edits preserve the existing secret", () => WithRepository((repo, _) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "保留的密码");
        var before = Protected(repo, account.Id)!;
        var edited = account with { Nickname = "新的昵称", Notes = "普通备注" };
        repo.SaveAccount(edited); Save(service, edited with { Notes = "再修改" }, null);
        Sequence(before, Protected(repo, account.Id)!); Equal("保留的密码", Read(service, account.Id));
        Equal("再修改", repo.GetAccounts().Single().Notes);
    })),
    ("explicit password replacement updates one account and one secret", () => WithRepository((repo, path) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "第一份密码");
        Save(service, account with { Nickname = "改密" }, " 第二份密码 ");
        Equal(" 第二份密码 ", Read(service, account.Id));
        Equal(1, repo.GetAccounts().Count); Equal(1L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        Equal("改密", repo.GetAccounts().Single().Nickname);
    })),
    ("empty passwords reject without adding accounts or replacing secrets", () => WithRepository((repo, path) =>
    {
        var service = NewService(repo); var fresh = Account(); Throws<ArgumentException>(() => Save(service, fresh, ""));
        Equal(0, repo.GetAccounts().Count); Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        var account = Account(); Save(service, account, "existing");
        var blob = Protected(repo, account.Id)!;
        Throws<ArgumentException>(() => Save(service, account with { Nickname = "不能写入" }, ""));
        Equal(account, repo.GetAccounts().Single()); Sequence(blob, Protected(repo, account.Id)!);
        Save(service, account, "   "); Equal("   ", Read(service, account.Id));
    })),
    ("account-bound ciphertext cannot decrypt after swapping two account blobs", () => WithRepository((repo, _) =>
    {
        var service = NewService(repo); var first = Account("600011"); var second = Account("600012");
        Save(service, first, "first-secret"); Save(service, second, "second-secret");
        var firstBlob = Protected(repo, first.Id)!; var secondBlob = Protected(repo, second.Id)!;
        SaveProtected(repo, first, secondBlob); SaveProtected(repo, second, firstBlob);
        AssertDecryptionFailure(service, first.Id, "first-secret", "second-secret");
        AssertDecryptionFailure(service, second.Id, "first-secret", "second-secret");
    })),
    ("tampered ciphertext rejects with useful errors that contain no password", () => WithRepository((repo, _) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "Tamper-秘密-123");
        var blob = Protected(repo, account.Id)!; blob[^1] ^= 0x80;
        SaveProtected(repo, account, blob); AssertDecryptionFailure(service, account.Id, "Tamper-秘密-123");
    })),
    ("SQLite secret insert failure rolls back the new account too", () => WithRepository((repo, path) =>
    {
        Execute(path, "CREATE TRIGGER fail_secret_insert BEFORE INSERT ON account_secrets BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        var account = Account(); Throws<InvalidOperationException>(() => Save(NewService(repo), account, "rollback-password"));
        Equal(0, repo.GetAccounts().Count); Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        Execute(path, "DROP TRIGGER fail_secret_insert");
        var reopened = new DeskRepository(path); reopened.Initialize(); Save(NewService(reopened), account, "retry-password");
        Equal("retry-password", Read(NewService(reopened), account.Id));
    })),
    ("SQLite secret update failure preserves both previous account and password", () => WithRepository((repo, path) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "original-password");
        var original = Protected(repo, account.Id)!;
        Execute(path, "CREATE TRIGGER fail_secret_update BEFORE UPDATE ON account_secrets BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        Throws<InvalidOperationException>(() => Save(service, account with { Nickname = "must rollback" }, "replacement-password"));
        Equal(account, repo.GetAccounts().Single()); Sequence(original, Protected(repo, account.Id)!);
        Equal("original-password", Read(service, account.Id));
    })),
    ("duplicate identity and invalid phone association never leave a secret", () => WithRepository((repo, path) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "original-password");
        var duplicate = Account(account.Identifier); var invalidPhone = Account("600099") with { PhoneId = Guid.NewGuid() };
        Throws<InvalidOperationException>(() => Save(service, duplicate, "duplicate-password"));
        Throws<ArgumentException>(() => Save(service, invalidPhone, "foreign-key-password"));
        Equal<byte[]?>(null, Protected(repo, duplicate.Id)); Equal<byte[]?>(null, Protected(repo, invalidPhone.Id));
        Equal(account, repo.GetAccounts().Single()); Equal(1L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        Equal("original-password", Read(service, account.Id));
    })),
    ("secret foreign key rejects orphan rows", () => WithRepository((repo, path) =>
    {
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO account_secrets (account_id, protected_password) VALUES ($id, $blob)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D")); command.Parameters.AddWithValue("$blob", new byte[] { 1, 2, 3 });
        var error = Throws<SqliteException>(() => command.ExecuteNonQuery()); Equal(19, error.SqliteErrorCode);
        Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets")); Equal(0, repo.GetAccounts().Count);
    })),
    ("opaque blobs must be nonempty and bounded before any account mutation", () => WithRepository((repo, path) =>
    {
        var account = Account();
        Throws<ArgumentException>(() => SaveProtected(repo, account, null!));
        Throws<ArgumentException>(() => SaveProtected(repo, account, []));
        Throws<ArgumentException>(() => SaveProtected(repo, account, new byte[65_537]));
        Equal(0, repo.GetAccounts().Count); Equal(0L, Scalar(path, "SELECT COUNT(*) FROM account_secrets"));
        var upperBound = new byte[65_536]; upperBound[0] = 1;
        SaveProtected(repo, account, upperBound); Sequence(upperBound, Protected(repo, account.Id)!);
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE account_secrets SET protected_password=$blob WHERE account_id=$id";
        command.Parameters.AddWithValue("$id", account.Id.ToString("D")); command.Parameters.AddWithValue("$blob", Array.Empty<byte>());
        Throws<SqliteException>(() => command.ExecuteNonQuery()); Sequence(upperBound, Protected(repo, account.Id)!);
    })),
    ("ciphertext read is detached and does not expose a mutable cached secret", () => WithRepository((repo, _) =>
    {
        var account = Account(); var service = NewService(repo); Save(service, account, "detached-password");
        var blob = Protected(repo, account.Id)!; Array.Clear(blob);
        Equal("detached-password", Read(service, account.Id));
    })),
    ("password plaintext is absent from account fields, database and active WAL bytes", () => WithRepository((repo, path) =>
    {
        const string password = "NeverPersistPlaintext-秘密-😀-'\"-0123456789-abcdefghijklmnopqrstuvwxyz";
        using var anchor = Open(path); using (var command = anchor.CreateCommand())
        { command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;"; command.ExecuteNonQuery(); }
        var account = Account(); Save(NewService(repo), account, password);
        Check(File.Exists(path + "-wal") && new FileInfo(path + "-wal").Length > 0, "fixture must inspect an active WAL");
        Equal(account, repo.GetAccounts().Single());
        foreach (var file in new[] { path, path + "-wal" })
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var contents = new MemoryStream(); stream.CopyTo(contents); var bytes = contents.ToArray();
            foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
                Check(bytes.AsSpan().IndexOf(encoding.GetBytes(password)) < 0, "plaintext password persisted to a database file");
        }
    }))
};

var allCases = cases.Concat(DraftChecks.Cases).ToArray();
var failures = 0;
foreach (var test in allCases)
{
    try { test.Test(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"RESULT {allCases.Length - failures}/{allCases.Length} passed; {failures} failed");
return failures == 0 ? 0 : 1;

static void WithDatabase(Action<string> test)
{
    var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDesk.Credentials.Tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try { test(Path.Combine(directory, "records.sqlite3")); }
    finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
}
static void WithRepository(Action<DeskRepository, string> test) => WithDatabase(path =>
{ var repo = new DeskRepository(path); repo.Initialize(); test(repo, path); });
static AccountRecord Account(string identifier = "600001") => new() { Identifier = identifier, Nickname = "昵称" };
static SqliteConnection Open(string path)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
    connection.Open(); return connection;
}
static void Execute(string path, string sql)
{ using var connection = Open(path); using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
static object? Scalar(string path, string sql)
{ using var connection = Open(path); using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { Check(EqualityComparer<T>.Default.Equals(expected, actual), "Values differ (contents suppressed)"); }
static void Sequence(byte[] expected, byte[] actual) => Check(expected.AsSpan().SequenceEqual(actual), "Protected blobs differ");
static T Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T error) { return error; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static void AssertDecryptionFailure(AccountCredentialService service, Guid accountId, params string[] passwords)
{
    var error = Throws<InvalidOperationException>(() => Read(service, accountId));
    Check(error.Message.Any(c => c is >= '\u4e00' and <= '\u9fff'), "decryption message should offer useful Chinese guidance");
    Check(error.Message.Contains("Windows", StringComparison.OrdinalIgnoreCase), "message should explain the Windows user binding");
    foreach (var password in passwords) Check(!error.ToString().Contains(password, StringComparison.Ordinal), "error exposed a password");
}
static AccountCredentialService NewService(DeskRepository repository) => new(repository);
static void Save(AccountCredentialService service, AccountRecord account, string? password) => service.Save(account, password);
static string? Read(AccountCredentialService service, Guid id) => service.ReadPassword(id);
static byte[]? Protected(DeskRepository repo, Guid id) => repo.GetProtectedPassword(id);
static void SaveProtected(DeskRepository repo, AccountRecord account, byte[] blob) => repo.SaveAccount(account, blob);

partial class Program
{
    // The original v1 schema is independent of the production initializer and has no credentials table.
    private const string LegacySchema = """
        CREATE TABLE phones (
            id TEXT PRIMARY KEY, number TEXT NOT NULL, label TEXT NOT NULL, official_restriction_note TEXT NOT NULL,
            restriction_until TEXT NULL, created_at TEXT NOT NULL
        );
        CREATE TABLE accounts (
            id TEXT PRIMARY KEY, provider INTEGER NOT NULL CHECK(provider IN (0, 1)),
            identifier TEXT NOT NULL COLLATE NOCASE, nickname TEXT NOT NULL,
            phone_id TEXT NULL REFERENCES phones(id), stage INTEGER NOT NULL CHECK(stage IN (0, 1, 2)),
            created_at TEXT NOT NULL, expires_at TEXT NULL,
            cancellation_state INTEGER NOT NULL CHECK(cancellation_state BETWEEN 0 AND 5),
            cancellation_requested_at TEXT NULL, cancellation_completed_at TEXT NULL, official_wait_until TEXT NULL,
            official_notice TEXT NOT NULL, notes TEXT NOT NULL, UNIQUE(provider, identifier)
        );
        CREATE TABLE redemptions (
            id TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id), code TEXT NOT NULL,
            outcome INTEGER NOT NULL CHECK(outcome IN (0, 1, 2)), redeemed_at TEXT NOT NULL,
            granted_duration_text TEXT NOT NULL, expires_at TEXT NULL, result_note TEXT NOT NULL,
            account_effect_applied INTEGER NOT NULL DEFAULT 0 CHECK(account_effect_applied IN (0, 1))
        );
        CREATE INDEX ix_redemptions_account_id ON redemptions(account_id);
        PRAGMA user_version = 1;
        """;
}
