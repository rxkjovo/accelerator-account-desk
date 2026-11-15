using System.IO;
using System.Text;
using AcceleratorDesk.App.Services;
using DraftData = AcceleratorDesk.App.Services.QqRegistrationDraft;

internal static class DraftChecks
{
    public static readonly (string Name, Action Test)[] Cases =
    [
        ("draft load returns null when absent without creating files", () => WithDirectory(directory =>
        {
            var target = Path.Combine(directory, "not-created"); var store = Store(target);
            Equal<DraftData?>(null, Load(store)); Check(!Directory.Exists(target), "load created a data directory");
        })),
        ("pending draft survives restart with exact Unicode password and submission state", () => WithDirectory(directory =>
        {
            var draft = Draft() with { Password = "  草稿'\"😀\tAa9!\n  ", SubmissionAttempted = true };
            Save(Store(directory), draft); Equal(draft, Load(Store(directory)));
            Equal(1, Directory.GetFiles(directory).Length);
        })),
        ("draft password phone and nickname never appear in on-disk bytes", () => WithDirectory(directory =>
        {
            var draft = Draft() with { PhoneNumber = "13800138000", Nickname = "敏感昵称-草稿-test", Password = "NeverPersist-Draft秘密😀-0123456789-abcdefghijklmnopqrstuvwxyz" };
            Save(Store(directory), draft);
            foreach (var path in Directory.GetFiles(directory))
            {
                var bytes = File.ReadAllBytes(path);
                foreach (var plaintext in new[] { draft.Password, draft.Nickname, draft.PhoneNumber })
                    foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
                        Check(bytes.AsSpan().IndexOf(encoding.GetBytes(plaintext)) < 0, "draft plaintext persisted on disk");
            }
        })),
        ("valid draft replacement persists one new encrypted file and retains unrelated files", () => WithDirectory(directory =>
        {
            var store = Store(directory); var first = Draft(); Save(store, first);
            var unrelated = Path.Combine(directory, "records.sqlite3"); File.WriteAllText(unrelated, "retain me");
            var replacement = first with { Nickname = "新昵称", SubmissionAttempted = true, Password = "更新密码" };
            Save(store, replacement); Equal(replacement, Load(Store(directory)));
            Equal("retain me", File.ReadAllText(unrelated)); Equal(2, Directory.GetFiles(directory).Length);
        })),
        ("tampered drafts report recovery guidance and cannot be silently overwritten", () => WithDirectory(directory =>
        {
            var store = Store(directory); var draft = Draft(); Save(store, draft);
            var path = Path.Combine(directory, "qq-registration-draft.bin"); var bytes = File.ReadAllBytes(path); bytes[^1] ^= 0x80;
            File.WriteAllBytes(path, bytes);
            UsefulFailure(() => Load(store), draft.Password);
            UsefulFailure(() => Save(store, draft with { Nickname = "do not overwrite" }), draft.Password);
            Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "unreadable draft was overwritten");
        })),
        ("empty or oversized draft files report useful errors and remain untouched", () => WithDirectory(directory =>
        {
            var store = Store(directory); var path = Path.Combine(directory, "qq-registration-draft.bin");
            foreach (var length in new[] { 0, 262_145 })
            {
                var original = new byte[length]; File.WriteAllBytes(path, original);
                UsefulFailure(() => Load(store), "unused-password");
                UsefulFailure(() => Save(store, Draft()), "unused-password");
                Check(original.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "invalid draft was overwritten");
            }
        })),
        ("failed atomic draft replacement preserves the original and removes its temporary file", () => WithDirectory(directory =>
        {
            var store = Store(directory); var original = Draft(); Save(store, original);
            var path = Path.Combine(directory, "qq-registration-draft.bin"); var bytes = File.ReadAllBytes(path);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            try
            {
                UsefulFailure(() => Save(store, original with { Password = "must-not-replace" }), original.Password);
                Check(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "failed replacement changed original ciphertext");
                Equal(original, Load(store)); Equal(1, Directory.GetFiles(directory).Length);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        })),
        ("draft text representation suppresses password and phone details", () =>
        {
            var draft = Draft(); var text = draft.ToString();
            Check(!text.Contains(draft.Password, StringComparison.Ordinal), "draft ToString exposed a password");
            Check(!text.Contains(draft.PhoneNumber, StringComparison.Ordinal), "draft ToString exposed a phone number");
        }),
        ("unreadable drafts reject load and overwrite without deleting the original", () => WithDirectory(directory =>
        {
            var store = Store(directory); var draft = Draft(); Save(store, draft);
            var path = Path.Combine(directory, "qq-registration-draft.bin"); var original = File.ReadAllBytes(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                UsefulFailure(() => Load(store), draft.Password);
                UsefulFailure(() => Save(store, draft), draft.Password);
            }
            Check(original.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "unreadable original draft changed");
        })),
        ("invalid draft identities and required fields reject before persistence", () => WithDirectory(directory =>
        {
            var store = Store(directory); var valid = Draft();
            var invalid = new[]
            {
                valid with { AccountId = Guid.Empty }, valid with { PhoneId = Guid.Empty },
                valid with { PhoneNumber = "" }, valid with { PhoneNumber = "   " },
                valid with { Nickname = "" }, valid with { Nickname = "   " }, valid with { Password = "" }
            };
            foreach (var draft in invalid) Throws<ArgumentException>(() => Save(store, draft));
            Equal(0, Directory.GetFiles(directory).Length);
            Save(store, valid);
            foreach (var draft in invalid) Throws<ArgumentException>(() => Save(store, draft));
            Equal(valid, Load(store));
        })),
        ("draft Clear removes only the owned pending file and is idempotent", () => WithDirectory(directory =>
        {
            var store = Store(directory); Save(store, Draft());
            var unrelated = Path.Combine(directory, "qq-registration-draft.bin.keep"); File.WriteAllText(unrelated, "preserve");
            var temporaryLookalike = Path.Combine(directory, "qq-registration-draft.bin.other.tmp"); File.WriteAllText(temporaryLookalike, "also preserve");
            Clear(store); Clear(store); Equal<DraftData?>(null, Load(store));
            Equal("preserve", File.ReadAllText(unrelated)); Equal("also preserve", File.ReadAllText(temporaryLookalike));
            Equal(2, Directory.GetFiles(directory).Length);
        }))
    ];

    private static DraftData Draft() => new()
    {
        AccountId = Guid.NewGuid(), PhoneId = Guid.NewGuid(), PhoneNumber = "13800138000", Nickname = "草稿昵称", Password = "generated-password-123!",
        SubmissionAttempted = false, CreatedAt = new DateTimeOffset(2026, 11, 15, 12, 34, 56, TimeSpan.FromHours(8))
    };
    private static void WithDirectory(Action<string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "AcceleratorDesk.Draft.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        try { test(path); } finally { Directory.Delete(path, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), "Values differ (contents suppressed)");
    private static T Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new Exception($"Expected {typeof(T).Name}"); }
    private static void UsefulFailure(Action action, string password)
    {
        var error = Throws<InvalidOperationException>(action);
        Check(error.Message.Any(c => c is >= '\u4e00' and <= '\u9fff'), "draft error should give useful Chinese guidance");
        Check(!error.ToString().Contains(password, StringComparison.Ordinal), "error exposed draft password");
    }
    private static QqRegistrationDraftStore Store(string directory) => new(directory);
    private static void Save(QqRegistrationDraftStore store, DraftData draft) => store.Save(draft);
    private static DraftData? Load(QqRegistrationDraftStore store) => store.Load();
    private static void Clear(QqRegistrationDraftStore store) => store.Clear();
}
