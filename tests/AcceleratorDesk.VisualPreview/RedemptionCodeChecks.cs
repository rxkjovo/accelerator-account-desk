using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AcceleratorDesk.App;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.App.Services;

internal static class RedemptionCodeChecks
{
    public static void Run(string root)
    {
        var passed = 0; var failed = 0;
        void Check(string name, Action<string> action)
        {
            var directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try { action(directory); passed++; Console.WriteLine("PASS REDEMPTION CODE " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL REDEMPTION CODE " + name + ": " + error.Message); }
        }
        Check("supplied screenshot codes load as unverified without creating user files", directory =>
        {
            var entries = Load(CreateStore(directory));
            foreach (var code in new[] { "JSQPHB", "XL888", "XL7878", "XL1314", "XL6789", "XL8989", "ws1997" })
                Equal(true, entries.Any(entry => entry.Code == code && entry.Note.Contains("未验证")));
            Equal(0, Directory.GetFiles(directory).Length);
        });
        Check("added code survives reopening with exact case and no changes to screenshot codes", directory =>
        {
            var store = CreateStore(directory); Equal(true, store.Add("MyFixtureCode2"));
            var entries = Load(CreateStore(directory)); Equal(8, entries.Count); Equal("MyFixtureCode2", entries.Last().Code);
        });
        Check("duplicate additions preserve original text and do not add another code", directory =>
        {
            var store = CreateStore(directory); Equal(false, store.Add("jsqphb"));
            Equal(true, store.Add("MixedCaseFixture")); Equal(false, store.Add("mixedcasefixture"));
            Equal(8, Load(CreateStore(directory)).Count); Equal("MixedCaseFixture", Load(CreateStore(directory)).Last().Code);
        });
        Check("invalid code rejects before creating or replacing saved data", directory =>
        {
            var store = CreateStore(directory);
            foreach (var code in new[] { "", "  ", "line\nbreak", new string('x', 201) }) Reject(() => store.Add(code));
            Equal(0, Directory.GetFiles(directory).Length);
        });
        Check("corrupt saved code file is preserved and blocks overwriting it", directory =>
        {
            var path = Path.Combine(directory, "redemption-codes.json"); File.WriteAllText(path, "{broken-json");
            var store = CreateStore(directory); Reject(() => store.Load()); Reject(() => store.Add("NewFixture"));
            Equal("{broken-json", File.ReadAllText(path));
        });
        Check("native code button copies the exact displayed screenshot code", directory =>
        {
            string? copied = null; var form = CreateWindow(CreateStore(directory), value => copied = value);
            try { Button(form, "复制兑换码 JSQPHB").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); Equal("JSQPHB", copied); }
            finally { form.Close(); }
        });
        Check("native add refreshes a copyable code and persists it for the next window", directory =>
        {
            string? copied = null; var form = CreateWindow(CreateStore(directory), value => copied = value);
            try
            {
                Elements(form).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "新增兑换码").Text = "AddedFromForm2";
                Button(form, "保存兑换码").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Button(form, "复制兑换码 AddedFromForm2").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Equal("AddedFromForm2", copied); Equal(true, Load(CreateStore(directory)).Any(x => x.Code == "AddedFromForm2"));
            }
            finally { form.Close(); }
        });
        Console.WriteLine($"RESULT REDEMPTION CODE {passed}/{passed + failed} passed; {failed} failed");
        if (failed > 0) throw new Exception("Redemption code checks failed.");
    }
    private static RedemptionCodeStore CreateStore(string directory) => new(directory);
    private static Window CreateWindow(RedemptionCodeStore store, Action<string> copy) => new RedemptionCodesWindow(store, copy);
    private static IReadOnlyList<RedemptionCodeEntry> Load(RedemptionCodeStore store) => store.Load();
    private static IEnumerable<DependencyObject> Elements(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { yield return child; foreach (var nested in Elements(child)) yield return nested; }
    }
    private static Button Button(Window form, string name) => Elements(form).OfType<Button>().Single(x => AutomationProperties.GetName(x) == name);
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidDataException) { return; } throw new Exception("Invalid code data was accepted."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
}
