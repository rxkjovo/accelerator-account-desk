using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

internal static class RegistrationFormChecks
{
    public static void Run(string root)
    {
        var passed = 0;
        var failed = 0;
        void Check(string name, Action<DeskRepository, string, PhoneRecord> action)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var repository = new DeskRepository(Path.Combine(directory, "records.db")); repository.Initialize();
            var phone = new PhoneRecord { Number = "13800138000", Label = "表单测试" }; repository.SavePhone(phone);
            try { action(repository, directory, phone); passed++; Console.WriteLine("PASS REGISTRATION FORM " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL REGISTRATION FORM " + name + ": " + error.Message); }
        }
        Check("opening wizard leaves account list and encrypted draft empty", (repository, directory, phone) =>
        {
            var form = new QqRegistrationWindow(repository, directory);
            try
            {
                Equal(0, repository.GetAccounts().Count); Equal<QqRegistrationDraft?>(null, new QqRegistrationDraftStore(directory).Load());
                Equal(Visibility.Collapsed, Field<TextBox>(form, "已显示的注册密码").Visibility);
                Equal(true, Field<ComboBox>(form, "注册手机号").IsEnabled);
            }
            finally { form.Close(); }
        });
        Check("restored wizard locks matching phone and keeps generated password hidden", (repository, directory, phone) =>
        {
            var other = new PhoneRecord { Number = "13900139000", Label = "第二号码" }; repository.SavePhone(other);
            var draft = new QqRegistrationSession(repository, directory).Prepare(other);
            var form = new QqRegistrationWindow(repository, directory);
            try
            {
                var combo = Field<ComboBox>(form, "注册手机号"); Equal(false, combo.IsEnabled); Equal(other.Id, ((Choice<Guid>)combo.SelectedItem).Value);
                Equal(draft.Password, Field<PasswordBox>(form, "自动生成的密码").Password); Equal(Visibility.Collapsed, Field<TextBox>(form, "已显示的注册密码").Visibility);
            }
            finally { form.Close(); }
        });
        Check("manual fallback requires confirmation then saves actual QQ phone and password", (repository, directory, phone) =>
        {
            var draft = new QqRegistrationSession(repository, directory).Prepare(phone);
            var form = new QqRegistrationWindow(repository, directory);
            try
            {
                Field<TextBox>(form, "注册成功的 QQ 号").Text = "9081762354";
                try { Invoke(form, "SaveManual"); throw new Exception("Unconfirmed result saved."); } catch (ArgumentException) { }
                Equal(0, repository.GetAccounts().Count);
                Field<CheckBox>(form, "已核对官方注册成功").IsChecked = true;
                Field<CheckBox>(form, "本次使用软件生成的密码").IsChecked = true; Invoke(form, "SaveManual");
                var account = repository.GetAccounts().Single(); Equal("9081762354", account.Identifier); Equal(phone.Id, account.PhoneId); Equal(draft.Password, new AccountCredentialService(repository).ReadPassword(account.Id));
                Equal(account.Id, form.SavedAccountId);
            }
            finally { form.Close(); }
        });
        Check("password editor saves encrypted value without changing membership", (repository, directory, phone) =>
        {
            var account = new AccountRecord { Identifier = "9081762354", PhoneId = phone.Id }; repository.SaveAccount(account);
            var service = new AccountCredentialService(repository); var form = new CredentialDialog(account, service);
            try
            {
                Field<PasswordBox>(form, "密码").Password = "EditedFixtureOnly2"; Invoke(form, "SaveRecord");
                Equal("EditedFixtureOnly2", service.ReadPassword(account.Id)); Equal<DateTimeOffset?>(null, repository.GetAccounts().Single().ExpiresAt);
            }
            finally { form.Close(); }
        });
        Check("password reveal is opt-in and all password fields clear on close", (repository, directory, phone) =>
        {
            var account = new AccountRecord { Identifier = "9081762354" }; var service = new AccountCredentialService(repository); service.Save(account, "HiddenFixtureOnly2");
            var form = new CredentialDialog(account, service);
            var visible = Field<TextBox>(form, "已显示的密码"); var password = Field<PasswordBox>(form, "密码");
            Equal(Visibility.Collapsed, visible.Visibility); Equal("", visible.Text);
            Elements(form).OfType<CheckBox>().Single().IsChecked = true; Equal("HiddenFixtureOnly2", visible.Text);
            form.Close(); Equal("", visible.Text); Equal("", password.Password);
        });
        Check("closing unconfirmed wizard preserves encrypted draft without registered account", (repository, directory, phone) =>
        {
            var draft = new QqRegistrationSession(repository, directory).Prepare(phone);
            var form = new QqRegistrationWindow(repository, directory); form.Close();
            Equal(0, repository.GetAccounts().Count); Equal(draft.Password, new QqRegistrationDraftStore(directory).Load()!.Password);
        });
        Check("restored changed phone displays the draft number and recovery guidance", (repository, directory, phone) =>
        {
            var draft = new QqRegistrationSession(repository, directory).Prepare(phone);
            var initial = new QqRegistrationWindow(repository, directory); initial.Close();
            repository.SavePhone(phone with { Number = "13900139000" }); var form = new QqRegistrationWindow(repository, directory);
            try
            {
                var choice = (Choice<Guid>)Field<ComboBox>(form, "注册手机号").SelectedItem;
                Equal(phone.Id, choice.Value); CheckText(choice.Label.Contains(AccountPresentation.MaskPhone(draft.PhoneNumber), StringComparison.Ordinal), "restored combo displays edited number instead of draft number");
                CheckText(!choice.Label.Contains(AccountPresentation.MaskPhone("13900139000"), StringComparison.Ordinal), "restored combo falsely displays another number");
                CheckText(Field<TextBlock>(form, "注册进度").Text.Contains("已更改", StringComparison.Ordinal), "restored mismatch recovery guidance missing");
                Field<TextBox>(form, "注册成功的 QQ 号").Text = "9081762354";
                Field<CheckBox>(form, "已核对官方注册成功").IsChecked = true; Field<CheckBox>(form, "本次使用软件生成的密码").IsChecked = true;
                Reject(() => Invoke(form, "SaveManual")); Equal(0, repository.GetAccounts().Count);
                Equal(draft, new QqRegistrationDraftStore(directory).Load());
            }
            finally { form.Close(); }
        });
        Check("active draft disables Add and keeps its selection when another phone is added", (repository, directory, phone) =>
        {
            new QqRegistrationSession(repository, directory).Prepare(phone); var form = new QqRegistrationWindow(repository, directory);
            try
            {
                Equal(false, Elements(form).OfType<Button>().Single(x => Equals(x.Content, "添加")).IsEnabled);
                var other = new PhoneRecord { Number = "13900139000" }; repository.SavePhone(other); Invoke(form, "RefreshPhones", other.Id);
                Equal(phone.Id, ((Choice<Guid>)Field<ComboBox>(form, "注册手机号").SelectedItem).Value);
            }
            finally { form.Close(); }
        });
        Check("manual form requires explicit password choice and saves actual differing password", (repository, directory, phone) =>
        {
            var draft = new QqRegistrationSession(repository, directory).Prepare(phone); var form = new QqRegistrationWindow(repository, directory);
            try
            {
                Field<TextBox>(form, "注册成功的 QQ 号").Text = "9081762354"; Field<CheckBox>(form, "已核对官方注册成功").IsChecked = true;
                Reject(() => Invoke(form, "SaveManual")); Equal(0, repository.GetAccounts().Count); Equal(draft, new QqRegistrationDraftStore(directory).Load());
                Equal(false, Field<CheckBox>(form, "本次使用软件生成的密码").IsChecked == true);
                var actual = Field<PasswordBox>(form, "本次实际使用的密码"); actual.Password = "  actual 密码 😀  "; Invoke(form, "SaveManual");
                Equal("  actual 密码 😀  ", new AccountCredentialService(repository).ReadPassword(draft.AccountId));
                form.Close(); Equal("", actual.Password);
            }
            finally { form.Close(); }
        });
        foreach (var swapped in new[] { false, true })
        {
            Check((swapped ? "cross-account" : "tampered") + " ciphertext editor opens hidden, cancel and empty save preserve, explicit replacement repairs", (repository, directory, phone) =>
            {
                var account = new AccountRecord { Identifier = "9081762354", PhoneId = phone.Id }; var service = new AccountCredentialService(repository); service.Save(account, "original-password");
                byte[] broken;
                if (swapped)
                {
                    var other = new AccountRecord { Identifier = "9081762355" }; service.Save(other, "other-password"); broken = repository.GetProtectedPassword(other.Id)!;
                }
                else { broken = repository.GetProtectedPassword(account.Id)!; broken[^1] ^= 0x80; }
                repository.SaveAccount(account, broken); var form = new CredentialDialog(account, service);
                try
                {
                    Equal("", Field<PasswordBox>(form, "密码").Password); Equal(Visibility.Collapsed, Field<TextBox>(form, "已显示的密码").Visibility);
                    CheckText(Field<TextBlock>(form, "密码恢复提示").Text.Contains("Windows", StringComparison.Ordinal), "decryption recovery guidance missing");
                    Reject(() => Invoke(form, "SaveRecord")); CheckText(broken.AsSpan().SequenceEqual(repository.GetProtectedPassword(account.Id)), "empty save changed ciphertext");
                }
                finally { form.Close(); }
                CheckText(broken.AsSpan().SequenceEqual(repository.GetProtectedPassword(account.Id)), "cancel changed ciphertext");
                var replacement = new CredentialDialog(account, service);
                try { Field<PasswordBox>(replacement, "密码").Password = "  repaired 密码 😀  "; Invoke(replacement, "SaveRecord"); Equal("  repaired 密码 😀  ", service.ReadPassword(account.Id)); }
                finally { replacement.Close(); }
            });
        }
        Console.WriteLine($"RESULT REGISTRATION FORM {passed}/{passed + failed} passed; {failed} failed");
        if (failed > 0) throw new Exception("Registration form checks failed.");
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var next in Elements(child)) yield return next; }
    }
    private static T Field<T>(Window form, string name) where T : FrameworkElement => Elements(form).OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Invoke(Window form, string method, params object?[] arguments)
    {
        try { form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void CheckText(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid form accepted."); }
}
