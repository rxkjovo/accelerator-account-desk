using System.Text;
using AcceleratorDesk.App.Services;

var failed = 0;
var total = 0;
void Test(string name, Action body)
{
    total++;
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid date was accepted"); }
Test("blank dates stay unknown", () => Equal<DateTimeOffset?>(null, Display.ParseDate("  ", "日期")));
Test("explicit local date round-trips without clock drift", () => Equal("2026-11-30 20:00", Display.DateInput(Display.ParseDate("2026-11-30 20:00", "日期"))));
Test("impossible and ambiguous input formats are rejected", () => { Reject(() => Display.ParseDate("2026-02-30 20:00", "日期")); Reject(() => Display.ParseDate("11/12/26", "日期")); });
Test("CSV preserves Chinese quotes multiline and escapes spreadsheet formulas", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "AcceleratorDeskCsv-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var file = Path.Combine(dir, "records.csv");
        CsvExporter.Write(file, new[] { new[] { "测试\"备注\"", "第一行\n第二行", "=1+1", " @SUM(1)", "普通账号" } });
        Equal("\"测试\"\"备注\"\"\",\"第一行\n第二行\",\"'=1+1\",\"' @SUM(1)\",\"普通账号\"" + Environment.NewLine, File.ReadAllText(file));
        Equal("EFBBBF", Convert.ToHexString(File.ReadAllBytes(file)[..3]));
    }
    finally { Directory.Delete(dir, true); }
});
Test("client setting persists and a fresh data directory starts empty", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "AcceleratorDeskSettings-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        var store = new SettingsStore(dir);
        Equal("", store.Load().ClientExecutable);
        store.Save(new DeskSettings { ClientExecutable = "C:\\测试目录\\client.exe" });
        Equal("C:\\测试目录\\client.exe", new SettingsStore(dir).Load().ClientExecutable);
    }
    finally { Directory.Delete(dir, true); }
});
Test("registration filling rejects deceptive and unrelated origins", () =>
{
    Equal(true, QqRegistrationPolicy.IsRegistrationPage("https://ssl.zc.qq.com/v5/index/?lang=zhCN"));
    foreach (var url in new[] { "http://ssl.zc.qq.com/v5/index/", "https://ssl.zc.qq.com.evil.test/v5/index/", "https://evil.test@ssl.zc.qq.com/v5/index/", "https://ssl.zc.qq.com:8443/v5/index/", "https://ssl.zc.qq.com/other/", "https://ti.qq.com/agreement/index.html", "file:///v5/index/", "", "invalid" })
        Equal(false, QqRegistrationPolicy.IsRegistrationPage(url));
});
Test("official navigation permits QQ HTTPS only without widening form filling", () =>
{
    Equal(true, QqRegistrationPolicy.IsOfficialNavigation("https://ti.qq.com/agreement/index.html"));
    Equal(false, QqRegistrationPolicy.IsRegistrationPage("https://ti.qq.com/agreement/index.html"));
    foreach (var url in new[] { "https://qq.com.evil.test/", "javascript:alert(1)", "http://qq.com/", "https://someone@qq.com/", "https://evil.test/" }) Equal(false, QqRegistrationPolicy.IsOfficialNavigation(url));
});
Test("phone selection normalizes supported numbers and rejects other country formats", () =>
{
    Equal("13800138000", QqRegistrationPolicy.NormalizePhone("+86 138-0013-8000"));
    Equal("13900139000", QqRegistrationPolicy.NormalizePhone("13900139000"));
    foreach (var value in new[] { "+1 2125551234", "138001380001", "01234567890", "abc13800138000", "" }) Reject(() => QqRegistrationPolicy.NormalizePhone(value));
});
Test("explicit success and an unambiguous QQ result are required before automatic account recording", () =>
{
    Equal("9081762354", QqRegistrationPolicy.ReadSuccessfulQq(QqRegistrationPolicy.RegistrationUrl, "注册成功\nQQ号码：9081762354\n手机号 13800138000", "13800138000"));
    foreach (var text in new[] { "欢迎注册QQ\nQQ号：9081762354", "如果注册成功\nQQ号：9081762354", "注册未成功\nQQ号：9081762354", "注册成功\n手机号 13800138000", "注册成功\nQQ号：13800138000", "注册成功\nQQ号：9081762354\nQQ号：8019624735", "注册成功\nQQ号：9081762354\n注册失败" })
        Equal<string?>(null, QqRegistrationPolicy.ReadSuccessfulQq(QqRegistrationPolicy.RegistrationUrl, text, "13800138000"));
    Equal<string?>(null, QqRegistrationPolicy.ReadSuccessfulQq("https://evil.test/", "注册成功\nQQ号：9081762354", "13800138000"));
});
Test("current official success layout reads an unlabeled QQ between success and recovery notice", () =>
{
    const string observedLayout = "QQ靓号\n简体中文\n意见反馈\n返回\n注册成功\n\n9081762354\n\uFFFC\n三天内未登录，该号码将会被收回。\n已安装QQ，立即登录\n下载QQ\nCopyright © 1998-2026 Tencent All Rights Reserved.";
    Equal("9081762354", QqRegistrationPolicy.ReadSuccessfulQq(QqRegistrationPolicy.RegistrationUrl, observedLayout, "13800138000"));
    Equal("9081762354", QqRegistrationPolicy.ReadSuccessfulQq(QqRegistrationPolicy.RegistrationUrl, observedLayout.Replace("\n", "\r\n"), "13800138000"));
    Equal<string?>(null, QqRegistrationPolicy.ReadSuccessfulQq("https://evil.test/", observedLayout, "13800138000"));
});
Test("unlabeled success rejects missing recovery context ambiguous numbers failure and phone results", () =>
{
    foreach (var text in new[] {
        "注册成功\n9081762354", "注册成功\n9081762354\n已安装QQ，立即登录\n下载QQ",
        "如果注册成功\n9081762354\n三天内未登录，该号码将会被收回。\n已安装QQ，立即登录\n下载QQ",
        "注册成功\n9081762354\n三天内未登录，该号码将会被收回。\n8019624735\n已安装QQ，立即登录\n下载QQ",
        "注册成功\n13800138000\n三天内未登录，该号码将会被收回。\n已安装QQ，立即登录\n下载QQ",
        "注册成功\n9081762354\n三天内未登录，该号码将会被收回。\n已安装QQ，立即登录\n下载QQ\n注册失败" })
        Equal<string?>(null, QqRegistrationPolicy.ReadSuccessfulQq(QqRegistrationPolicy.RegistrationUrl, text, "13800138000"));
});
Test("each registration gets an independent QQ-compatible generated password", () =>
{
    var generated = Enumerable.Range(0, 25).Select(_ => QqRegistrationPolicy.CreateCredentials()).ToList();
    Equal(25, generated.Select(x => x.Password).Distinct().Count());
    foreach (var value in generated)
    {
        if (value.Password.Length is < 8 or > 10 || !value.Password.Any(char.IsAsciiDigit) || !value.Password.Any(char.IsAsciiLetterLower) || !value.Password.Any(char.IsAsciiLetterUpper) || value.Password.Any(x => !char.IsAsciiLetterOrDigit(x))) throw new Exception("Generated password is too long for the requested shorter workflow or violates QQ's character requirements.");
        if (value.Nickname.Length is < 2 or > 24) throw new Exception("Nickname violates the official input limit.");
    }
});
Console.WriteLine($"RESULT {total - failed}/{total} passed; {failed} failed");
return failed == 0 ? 0 : 1;
