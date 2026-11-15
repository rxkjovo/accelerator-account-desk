using System.IO;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using Microsoft.Data.Sqlite;

internal static class RegistrationPhoneChecks
{
    public static readonly (string Name, Action Test)[] Cases =
    [
        ("registration save rejects stale expected phone after another repository edits the same ID", () => WithRepository((repo, path, phone) =>
        {
            var account = Account(phone.Id); var other = new DeskRepository(path); other.SavePhone(phone with { Number = "13900139000" });
            Reject(() => Save(repo, account, "actual-password", phone.Id, "13800138000"));
            Equal(0, repo.GetAccounts().Count); Equal<byte[]?>(null, repo.GetProtectedPassword(account.Id));
        })),
        ("registration guard accepts equivalent normalized phone formatting", () => WithRepository((repo, _, phone) =>
        {
            repo.SavePhone(phone with { Number = "+86 (138) 0013-8000" }); var account = Account(phone.Id);
            Save(repo, account, "  exact 秘密 😀  ", phone.Id, "13800138000");
            Equal("  exact 秘密 😀  ", new AccountCredentialService(repo).ReadPassword(account.Id));
        })),
        ("registration guard rejects a different account phone ID", () => WithRepository((repo, _, phone) =>
        {
            var other = new PhoneRecord { Number = phone.Number }; repo.SavePhone(other); var account = Account(other.Id);
            Reject(() => Save(repo, account, "actual-password", phone.Id, "13800138000"));
            Equal(0, repo.GetAccounts().Count); Equal<byte[]?>(null, repo.GetProtectedPassword(account.Id));
        })),
        ("transaction rechecks registration phone and rolls back trigger edits account and secret together", () => WithRepository((repo, path, phone) =>
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = "CREATE TRIGGER change_phone_after_account AFTER INSERT ON accounts BEGIN UPDATE phones SET number='13900139000'; END;"; command.ExecuteNonQuery();
            var account = Account(phone.Id); Reject(() => Save(repo, account, "actual-password", phone.Id, "13800138000"));
            Equal(0, repo.GetAccounts().Count); Equal<byte[]?>(null, repo.GetProtectedPassword(account.Id)); Equal(phone.Number, repo.GetPhones().Single().Number);
        })),
        ("stale registration update preserves existing account and ciphertext", () => WithRepository((repo, _, phone) =>
        {
            var account = Account(phone.Id); new AccountCredentialService(repo).Save(account, "old-password"); var original = repo.GetProtectedPassword(account.Id)!;
            repo.SavePhone(phone with { Number = "13900139000" });
            Reject(() => Save(repo, account with { Nickname = "must not save" }, "replacement-password", phone.Id, "13800138000"));
            Equal(account, repo.GetAccounts().Single()); Check(original.AsSpan().SequenceEqual(repo.GetProtectedPassword(account.Id)), "existing ciphertext changed");
        }))
    ];

    private static void WithRepository(Action<DeskRepository, string, PhoneRecord> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDeskPhoneGuard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "records.db"); var repository = new DeskRepository(path); repository.Initialize();
            var phone = new PhoneRecord { Number = "13800138000" }; repository.SavePhone(phone); test(repository, path, phone);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(directory, true); }
    }
    private static AccountRecord Account(Guid phoneId) => new() { Identifier = "9081762354", PhoneId = phoneId };
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), "Values differ (contents suppressed)");
    private static void Reject(Action action)
    { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; } throw new Exception("Stale registration phone accepted"); }
    private static void Save(DeskRepository repo, AccountRecord account, string password, Guid phoneId, string number)
        => new AccountCredentialService(repo).SaveForRegistration(account, password, phoneId, number);
}
