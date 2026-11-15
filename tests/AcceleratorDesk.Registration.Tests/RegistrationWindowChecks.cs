using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class RegistrationWindowChecks
{
    private const string Fixture = """
        <!doctype html><html><body><h1>本地窗口测试，不连接 QQ</h1>
        <input type="tel" placeholder="输入手机号码"><input placeholder="短信验证码">
        <input placeholder="输入昵称"><input type="password" placeholder="输入QQ密码">
        <div class="send-code-button" onclick="window.smsRequests++;window.requestedAt=Date.now();setTimeout(()=>document.body.insertAdjacentHTML('beforeend','<p>由于安全限制，需要您下载手机QQ注册账号</p>'),200)">发送验证码</div>
        <div class="agreement-checkbox" dt-params="is_check_agree=1"><div class="agreement-checkbox__box"></div></div>
        <button onclick="window.submissions++">立即注册</button>
        <script>window.smsRequests=0;window.submissions=0;</script></body></html>
        """;

    public static async Task Run(CoreWebView2Environment environment, string root)
    {
        var passed = 0;
        var failed = 0;
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); passed++; Console.WriteLine("PASS REGISTRATION WINDOW " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL REGISTRATION WINDOW " + name + ": " + error.Message); }
        }
        await Check("delayed mobile-only response stops SMS and submission while preserving encrypted draft across reopen", async () =>
        {
            var directory = Path.Combine(root, "window-" + Guid.NewGuid().ToString("N"));
            var repository = new DeskRepository(Path.Combine(directory, "records.db")); repository.Initialize();
            var phone = new PhoneRecord { Number = "13800138000", Label = "本地测试号码" }; repository.SavePhone(phone);
            var form = new QqRegistrationWindow(repository, directory) { Left = -12000, Top = -12000, WindowStartupLocation = WindowStartupLocation.Manual };
            try
            {
                // Intercept at the network boundary; the real window, session, timer and scripts still run.
                SetField(form, "_initialization", Task.CompletedTask);
                form.Show();
                var browser = Field<WebView2>(form, "_browser");
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                browser.CoreWebView2.WebResourceRequested += (_, args) => args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(Fixture)), 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                var loaded = new TaskCompletionSource();
                browser.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new Exception("Fixture navigation failed.")); };
                browser.CoreWebView2.Navigate(QqRegistrationPolicy.RegistrationUrl);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
                SetField(form, "_ready", true);
                await (Task)Invoke(form, "RunAsync", (Func<Task>)(() => (Task)Invoke(form, "StartAsync")!))!;
                var draft = new QqRegistrationDraftStore(directory).Load()!;
                var deadline = DateTimeOffset.UtcNow.AddSeconds(7);
                while (Field<Button>(form, "_submit").IsEnabled && DateTimeOffset.UtcNow < deadline) await Task.Delay(50);
                Assert(!Field<Button>(form, "_submit").IsEnabled, "Submission remained enabled after the official mobile-only response.");
                Assert(!Field<Button>(form, "_start").IsEnabled, "SMS remained enabled after the official restriction.");
                Assert(!Field<TextBox>(form, "_code").IsEnabled, "Code field remained enabled after the official restriction.");
                Assert(Field<Button>(form, "_manualSave").IsEnabled, "Explicit manual result saving should remain available.");
                Assert(Field<TextBlock>(form, "_status").Text.Contains("手机 QQ"), "Mobile-only guidance missing.");
                Assert(!Field<System.Windows.Threading.DispatcherTimer>(form, "_poll").IsEnabled, "Read-only monitor did not stop after a definitive restriction.");
                Equal(0, repository.GetAccounts().Count);
                Equal(draft.Password, new QqRegistrationDraftStore(directory).Load()!.Password);
                Equal(1, int.Parse(await browser.ExecuteScriptAsync("window.smsRequests")));
                Field<TextBox>(form, "_code").Text = "123456";
                await (Task)Invoke(form, "RunAsync", (Func<Task>)(() => (Task)Invoke(form, "SubmitAsync")!))!;
                Equal(0, int.Parse(await browser.ExecuteScriptAsync("window.submissions")));
                await (Task)Invoke(form, "RunAsync", (Func<Task>)(() => (Task)Invoke(form, "StartAsync")!))!;
                Equal(1, int.Parse(await browser.ExecuteScriptAsync("window.smsRequests")));
                form.Close();
                var restored = new QqRegistrationWindow(repository, directory);
                try
                {
                    Assert(!Field<Button>(restored, "_start").IsEnabled && !Field<Button>(restored, "_submit").IsEnabled, "Reopening forgot the official restriction.");
                    Equal(draft.Password, Field<PasswordBox>(restored, "_password").Password);
                    Field<TextBox>(restored, "_qq").Text = "9081762354";
                    Field<CheckBox>(restored, "_confirmed").IsChecked = true;
                    await Check("mobile fallback never infers desktop password and preserves explicitly entered actual password", () =>
                    {
                        try { Invoke(restored, "SaveManual"); throw new Exception("A generated password was assumed after mobile registration."); }
                        catch (ArgumentException) { }
                        Equal(0, repository.GetAccounts().Count);
                        Field<PasswordBox>(restored, "_manualPassword").Password = "ActualMobileFixture2";
                        Invoke(restored, "SaveManual");
                        Equal("ActualMobileFixture2", new AccountCredentialService(repository).ReadPassword(draft.AccountId));
                        return Task.CompletedTask;
                    });
                }
                finally { restored.Close(); }
            }
            finally { form.Close(); }
        });
        Console.WriteLine($"RESULT REGISTRATION WINDOW {passed}/{passed + failed} passed; {failed} failed; all requests intercepted by local fixtures");
        if (failed > 0) throw new Exception("Registration window checks failed.");
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object[] arguments)
    {
        try { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
}
