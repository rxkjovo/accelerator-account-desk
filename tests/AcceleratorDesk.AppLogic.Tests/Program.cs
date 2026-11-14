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
Console.WriteLine($"RESULT {total - failed}/{total} passed; {failed} failed");
return failed == 0 ? 0 : 1;
