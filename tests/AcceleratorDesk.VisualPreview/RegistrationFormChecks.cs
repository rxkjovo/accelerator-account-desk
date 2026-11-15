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
        void Check(string name, Action<DeskRepository, string, PhoneRecord> action)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var repository = new DeskRepository(Path.Combine(directory, "records.db")); repository.Initialize();
            var phone = new PhoneRecord { Number = "13800138000", Label = "表单测试" }; repository.SavePhone(phone);
            action(repository, directory, phone); passed++; Console.WriteLine("PASS REGISTRATION FORM " + name);
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
                Field<CheckBox>(form, "已核对官方注册成功").IsChecked = true; Invoke(form, "SaveManual");
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
        Console.WriteLine($"RESULT REGISTRATION FORM {passed}/6 passed");
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        { yield return child; foreach (var next in Elements(child)) yield return next; }
    }
    private static T Field<T>(Window form, string name) where T : FrameworkElement => Elements(form).OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Invoke(Window form, string method)
    {
        try { form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
}
