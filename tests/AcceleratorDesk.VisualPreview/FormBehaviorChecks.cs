using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.Core;

internal static class FormBehaviorChecks
{
    public static void Run(string databasePath)
    {
        var repository = new DeskRepository(databasePath);
        repository.Initialize();
        var windows = new List<Window>();
        var passed = 0;
        void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS FORM " + name); }
        T Keep<T>(T window) where T : Window { windows.Add(window); return window; }
        try
        {
            Check("phone form saves supplied number and unknown restriction", () =>
            {
                var form = Keep(new PhoneDialog(null, repository.SavePhone));
                Field<TextBox>(form, "手机号 *").Text = "13800138000";
                Field<TextBox>(form, "号码备注").Text = "本地表单测试";
                Save(form);
                Equal("13800138000", repository.GetPhones().Single().Number);
                Equal<DateTimeOffset?>(null, repository.GetPhones().Single().RestrictionUntil);
            });
            Check("account form saves provider phone association and literal expiry", () =>
            {
                var form = Keep(new AccountDialog(null, repository.GetPhones(), repository.SaveAccount));
                Choose(form, "登录渠道", AccountProvider.Weibo);
                Field<TextBox>(form, "QQ 号 / 微博账号标识 *").Text = "form_demo_weibo";
                Field<TextBox>(form, "账号备注").Text = "表单保存测试";
                Choose<Guid?>(form, "关联手机号", repository.GetPhones().Single().Id);
                Field<TextBox>(form, "实际到期时间").Text = "2026-11-30 20:00";
                Save(form);
                var account = repository.GetAccounts().Single();
                Equal(AccountProvider.Weibo, account.Provider);
                Equal<Guid?>(repository.GetPhones().Single().Id, account.PhoneId);
                Equal("2026-11-30 20:00", account.ExpiresAt!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            });
            Check("unsaved account edits do not mutate original record", () =>
            {
                var original = repository.GetAccounts().Single();
                var form = Keep(new AccountDialog(original, repository.GetPhones(), repository.SaveAccount));
                Field<TextBox>(form, "账号备注").Text = "未保存的修改";
                Equal("表单保存测试", original.Nickname);
                Equal("表单保存测试", repository.GetAccounts().Single().Nickname);
            });
            Check("completed cancellation without official completion date is rejected", () =>
            {
                var form = Keep(new AccountDialog(repository.GetAccounts().Single(), repository.GetPhones(), repository.SaveAccount));
                Choose(form, "平台账号注销状态", CancellationStatus.Completed);
                Reject(() => Save(form));
                Equal(CancellationStatus.None, repository.GetAccounts().Single().CancellationState);
            });
            Check("recorded cancellation retains account and supplied completion", () =>
            {
                var form = Keep(new AccountDialog(repository.GetAccounts().Single(), repository.GetPhones(), repository.SaveAccount));
                Choose(form, "平台账号注销状态", CancellationStatus.Completed);
                Field<TextBox>(form, "注销申请时间").Text = "2026-11-09 20:00";
                Field<TextBox>(form, "官方确认注销完成时间").Text = "2026-11-10 20:00";
                Save(form);
                Equal(CancellationStatus.Completed, repository.GetAccounts().Single().CancellationState);
                Equal(1, repository.GetAccounts().Count);
            });
            var fresh = new AccountRecord { Identifier = "909090909", Provider = AccountProvider.QQ };
            repository.SaveAccount(fresh);
            Check("failed redemption form does not invent credit or advance stage", () =>
            {
                var form = Keep(new RedemptionDialog(null, repository.GetAccounts(), fresh.Id, repository.SaveRedemption));
                Field<TextBox>(form, "兑换码 / 口令 *").Text = "DEMO_FAILED";
                Choose(form, "官方返回结果", RedemptionOutcome.Failed);
                Save(form);
                var account = repository.GetAccounts().Single(x => x.Id == fresh.Id);
                Equal(AccountStage.Registered, account.Stage);
                Equal<DateTimeOffset?>(null, account.ExpiresAt);
            });
            Check("successful redemption form stores actual expiry", () =>
            {
                var form = Keep(new RedemptionDialog(null, repository.GetAccounts(), fresh.Id, repository.SaveRedemption));
                Field<TextBox>(form, "兑换码 / 口令 *").Text = "DEMO_SUCCESS";
                Choose(form, "官方返回结果", RedemptionOutcome.Succeeded);
                Field<TextBox>(form, "实际到期时间").Text = "2026-12-15 21:30";
                Save(form);
                var account = repository.GetAccounts().Single(x => x.Id == fresh.Id);
                Equal(AccountStage.Redeemed, account.Stage);
                Equal("2026-12-15 21:30", account.ExpiresAt!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            });
            Check("malformed date leaves previous account data intact", () =>
            {
                var account = repository.GetAccounts().Single(x => x.Id == fresh.Id);
                var form = Keep(new AccountDialog(account, repository.GetPhones(), repository.SaveAccount));
                Field<TextBox>(form, "实际到期时间").Text = "2026-02-30 21:30";
                Reject(() => Save(form));
                Equal(account.ExpiresAt, repository.GetAccounts().Single(x => x.Id == fresh.Id).ExpiresAt);
            });
            Check("editing historical successful redemption does not replay expiry", () =>
            {
                var redemption = repository.GetRedemptions(fresh.Id).Single(x => x.Outcome == RedemptionOutcome.Succeeded);
                var form = Keep(new RedemptionDialog(redemption, repository.GetAccounts(), fresh.Id, repository.SaveRedemption));
                Field<TextBox>(form, "实际到期时间").Text = "2027-01-15 21:30";
                Save(form);
                Equal("2026-12-15 21:30", repository.GetAccounts().Single(x => x.Id == fresh.Id).ExpiresAt!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            });
            Console.WriteLine($"RESULT FORM {passed}/9 passed");
        }
        finally { foreach (var window in windows) window.Close(); }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var next in LogicalDescendants(child)) yield return next;
        }
    }
    private static T Field<T>(Window form, string name) where T : FrameworkElement => LogicalDescendants(form).OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Choose<T>(Window form, string name, T value)
    {
        var combo = Field<ComboBox>(form, name);
        combo.SelectedItem = combo.Items.OfType<Choice<T>>().Single(x => EqualityComparer<T>.Default.Equals(x.Value, value));
    }
    private static void Save(Window form)
    {
        try { form.GetType().GetMethod("SaveRecord", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid form was saved."); }
}
