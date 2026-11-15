using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using AcceleratorDesk.App.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    private const string Fixture = """
        <!doctype html><html><body><h1>独立测试页面：不连接 QQ 服务</h1>
        <input type="tel" placeholder="输入手机号码"><input placeholder="短信验证码">
        <input placeholder="输入昵称"><input type="password" placeholder="输入QQ密码">
        <div class="send-code-button" onclick="window.smsRequests++"><span>发送验证码</span></div>
        <div class="agreement-checkbox" dt-params="is_check_agree=0"><div class="agreement-checkbox__box"></div>已阅读并同意</div>
        <button class="submit-button" disabled onclick="window.submissions++">立即注册</button>
        <script>window.smsRequests=0;window.submissions=0;window.inputs=0;
        document.querySelectorAll('input').forEach(e=>e.addEventListener('input',()=>window.inputs++));
        document.querySelector('.agreement-checkbox__box').onclick=()=>{document.querySelector('.agreement-checkbox').setAttribute('dt-params','is_check_agree=1');document.querySelector('button').disabled=false;};
        </script></body></html>
        """;

    private const string SuccessFixture = """
        <!doctype html><html><body><header>独立测试页面：不连接 QQ 服务</header>
        <nav>QQ靓号<br>简体中文<br>意见反馈</nav><main><p>返回</p><h1>注册成功</h1>
        <div>9081762354</div><p>三天内未登录，该号码将会被收回。</p>
        <button>已安装QQ，立即登录</button><button>下载QQ</button></main>
        <footer>Copyright © 1998-2026 Tencent All Rights Reserved.</footer></body></html>
        """;

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        app.Startup += async (_, _) =>
        {
            try { await Run(); exitCode = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exitCode;
    }

    private static async Task Run()
    {
        SessionChecks.Run();
        var directory = Path.Combine(Path.GetTempPath(), "AcceleratorDeskRegistration-" + Guid.NewGuid().ToString("N"));
        var browser = new WebView2();
        var host = new Window { Content = browser, Width = 800, Height = 600, Left = -12000, Top = -12000, ShowInTaskbar = false, Title = "注册桥接独立测试" };
        host.Show();
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, directory);
            await browser.EnsureCoreWebView2Async(environment);
            var fixtureDocument = Fixture;
            browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            browser.CoreWebView2.WebResourceRequested += (_, args) =>
            {
                var data = Encoding.UTF8.GetBytes(fixtureDocument);
                args.Response = environment.CreateWebResourceResponse(new MemoryStream(data), 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
            };
            async Task Navigate(string url)
            {
                var loaded = new TaskCompletionSource();
                void Complete(object? _, CoreWebView2NavigationCompletedEventArgs e) { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new Exception("Fixture navigation failed")); }
                browser.CoreWebView2.NavigationCompleted += Complete;
                try { browser.CoreWebView2.Navigate(url); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
                finally { browser.CoreWebView2.NavigationCompleted -= Complete; }
            }
            async Task<JsonElement> Execute(string script) => JsonDocument.Parse(await browser.ExecuteScriptAsync(script)).RootElement.Clone();
            var passed = 0;
            var total = 0;
            async Task Check(string name, Func<Task> action)
            {
                total++;
                try { await action(); passed++; Console.WriteLine("PASS BROWSER " + name); }
                catch (Exception ex) { Console.WriteLine("FAIL BROWSER " + name + ": " + ex.Message); }
            }
            await Navigate(QqRegistrationPolicy.RegistrationUrl);
            await Check("fill writes real fields and events without SMS agreement or submission", async () =>
            {
                var result = await Execute(QqPageBridge.Fill("13800138000", "测试昵称", "LocalFixtureOnly2"));
                Equal(true, result.GetProperty("ok").GetBoolean());
                var values = await Execute("({phone:document.querySelector('input[type=tel]').value,nickname:document.querySelector('input[placeholder=输入昵称]').value,password:document.querySelector('input[type=password]').value,sms:window.smsRequests,submissions:window.submissions,inputs:window.inputs,agreement:document.querySelector('.agreement-checkbox').getAttribute('dt-params')})");
                Equal("13800138000", values.GetProperty("phone").GetString()); Equal("测试昵称", values.GetProperty("nickname").GetString()); Equal("LocalFixtureOnly2", values.GetProperty("password").GetString());
                Equal(0, values.GetProperty("sms").GetInt32()); Equal(0, values.GetProperty("submissions").GetInt32()); Equal(3, values.GetProperty("inputs").GetInt32()); Equal("is_check_agree=0", values.GetProperty("agreement").GetString());
            });
            await Check("SMS request performs one explicit click", async () =>
            {
                var result = await Execute(QqPageBridge.RequestSms());
                Equal(true, result.GetProperty("ok").GetBoolean());
                var message = result.GetProperty("message").GetString() ?? "";
                Equal(false, message.Contains("已请求验证码") || message.Contains("已发送") || message.Contains("发送成功"));
                Equal(1, (await Execute("window.smsRequests")).GetInt32()); Equal(0, (await Execute("window.submissions")).GetInt32());
            });
            await Check("submission preparation without consent leaves agreement and code untouched", async () =>
            {
                Equal(false, (await Execute(QqPageBridge.PrepareSubmission("123456", false))).GetProperty("ok").GetBoolean());
                Equal("", (await Execute("document.querySelector('input[placeholder=短信验证码]').value")).GetString());
                Equal("is_check_agree=0", (await Execute("document.querySelector('.agreement-checkbox').getAttribute('dt-params')")).GetString());
            });
            await Check("explicit consent and matching phone submit once", async () =>
            {
                Equal(true, (await Execute(QqPageBridge.PrepareSubmission("123456", true))).GetProperty("ok").GetBoolean());
                Equal(true, (await Execute(QqPageBridge.Submit("13800138000"))).GetProperty("ok").GetBoolean());
                Equal("123456", (await Execute("document.querySelector('input[placeholder=短信验证码]').value")).GetString());
                Equal(1, (await Execute("window.submissions")).GetInt32());
            });
            await Check("phone mismatch prevents submitting another phone's registration", async () =>
            {
                Equal(false, (await Execute(QqPageBridge.Submit("13900139000"))).GetProperty("ok").GetBoolean());
                Equal(1, (await Execute("window.submissions")).GetInt32());
            });
            await Navigate("https://untrusted.example.test/fixture");
            await Check("foreign source cannot receive phone password SMS or registration actions", async () =>
            {
                foreach (var script in new[] { QqPageBridge.Fill("13800138000", "测试", "LocalFixtureOnly2"), QqPageBridge.RequestSms(), QqPageBridge.PrepareSubmission("123456", true), QqPageBridge.Submit("13800138000") })
                    Equal(false, (await Execute(script)).GetProperty("ok").GetBoolean());
                Equal("", (await Execute("document.querySelector('input[type=password]').value")).GetString());
                Equal(0, (await Execute("window.smsRequests")).GetInt32()); Equal(0, (await Execute("window.submissions")).GetInt32());
            });
            fixtureDocument = SuccessFixture;
            await Navigate(QqRegistrationPolicy.RegistrationUrl);
            await Check("visible text from the observed unlabeled success layout identifies its sole QQ", async () =>
            {
                var result = await Execute(QqPageBridge.VisibleText()); Equal(true, result.GetProperty("ok").GetBoolean());
                Equal("9081762354", QqRegistrationPolicy.ReadSuccessfulQq(browser.Source.AbsoluteUri, result.GetProperty("text").GetString()!, "13800138000"));
            });
            fixtureDocument = Fixture.Replace("window.smsRequests++", "window.smsRequests++;document.body.insertAdjacentHTML('beforeend','<p>由于安全限制，需要您下载手机QQ注册账号</p>')");
            await Navigate(QqRegistrationPolicy.RegistrationUrl);
            await Check("immediate mobile-only response rejects further actions without extra SMS or submissions", async () =>
            {
                var result = await Execute(QqPageBridge.RequestSms());
                Equal(false, result.GetProperty("ok").GetBoolean());
                Equal(true, result.GetProperty("requiresMobile").GetBoolean());
                foreach (var script in new[] { QqPageBridge.Fill("13800138000", "测试", "LocalFixtureOnly2"), QqPageBridge.RequestSms(), QqPageBridge.PrepareSubmission("123456", true), QqPageBridge.Submit("13800138000") })
                    Equal(false, (await Execute(script)).GetProperty("ok").GetBoolean());
                Equal(1, (await Execute("window.smsRequests")).GetInt32());
                Equal(0, (await Execute("window.submissions")).GetInt32());
                Equal(0, (await Execute("window.inputs")).GetInt32());
            });
            fixtureDocument = Fixture.Replace("<h1>", "<p hidden>由于安全限制，需要您下载手机QQ注册账号</p><p>下载QQ，已安装手机QQ请登录</p><h1>");
            await Navigate(QqRegistrationPolicy.RegistrationUrl);
            await Check("hidden mobile-only template and ordinary download text do not block an available form", async () =>
            {
                Equal(true, (await Execute(QqPageBridge.RequestSms())).GetProperty("ok").GetBoolean());
                Equal(1, (await Execute("window.smsRequests")).GetInt32());
            });
            Console.WriteLine($"RESULT BROWSER {passed}/{total} passed; {total - passed} failed; all requests intercepted by local fixtures");
            await RegistrationWindowChecks.Run(environment, directory);
            if (passed != total) throw new Exception("Registration bridge checks failed.");
        }
        finally { browser.Dispose(); host.Close(); }
    }

    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
}
