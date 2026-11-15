using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AcceleratorDesk.App;
using AcceleratorDesk.App.Dialogs;
using AcceleratorDesk.App.Services;
using AcceleratorDesk.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/visual-preview");
            Directory.CreateDirectory(output);
            var data = Path.Combine(output, "fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/AcceleratorAccountDesk;component/Theme.Dark.xaml", UriKind.Absolute)
            });
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var repository = new DeskRepository(Path.Combine(data, "preview.db"));
            repository.Initialize();
            var store = new SettingsStore(data);
            var empty = new MainWindow(repository, store, data);
            SetPreviewStatus(empty);
            Render(empty, Path.Combine(output, "home-empty.png"), 1320, 820);

            var now = DateTimeOffset.Now;
            var phone = new PhoneRecord { Number = "13800138000", Label = "示例号码 A", OfficialRestrictionNote = "示例：尚未记录官方限制期限" };
            repository.SavePhone(phone);
            var accounts = new[]
            {
                new AccountRecord { Identifier = "9081762354", Nickname = "日常游戏账号（示例）", Provider = AccountProvider.QQ, PhoneId = phone.Id, Stage = AccountStage.Redeemed, ExpiresAt = now.AddDays(12), Notes = "视觉预览示例，不是真实账号" },
                new AccountRecord { Identifier = "8019624735", Nickname = "备用账号（示例）", Provider = AccountProvider.QQ, PhoneId = phone.Id, Stage = AccountStage.Redeemed, ExpiresAt = now.AddDays(2), Notes = "视觉预览示例" },
                new AccountRecord { Identifier = "demo_weibo_03", Nickname = "历史账号（示例）", Provider = AccountProvider.Weibo, Stage = AccountStage.Authorized, ExpiresAt = now.AddDays(-1), CancellationState = CancellationStatus.Waiting, CancellationRequestedAt = now.AddDays(-1), OfficialNotice = "示例：等待官方结果，期限未知" }
            };
            foreach (var account in accounts) repository.SaveAccount(account);
            var sample = new MainWindow(repository, store, data);
            if (sample.FindName("AccountsGrid") is DataGrid grid) grid.SelectedIndex = 0;
            SetPreviewStatus(sample);
            Render(sample, Path.Combine(output, "home-sample.png"), 1320, 820);
            VerifyPrimaryAction(sample);
            Render(sample, Path.Combine(output, "home-compact.png"), 1100, 680);
            var editor = new AccountDialog(accounts[0], repository.GetPhones(), _ => { });
            Render(editor, Path.Combine(output, "account-editor.png"), 720, 680);
            VerifyInputs(editor);
            var phoneEditor = new PhoneDialog(phone, _ => { });
            Render(phoneEditor, Path.Combine(output, "phone-editor.png"), 720, 640);
            VerifyInputs(phoneEditor);
            var redemptionEditor = new RedemptionDialog(null, accounts, accounts[0].Id, _ => { });
            Render(redemptionEditor, Path.Combine(output, "redemption-editor.png"), 720, 680);
            VerifyInputs(redemptionEditor);
            var registration = new QqRegistrationWindow(repository, data);
            Render(registration, Path.Combine(output, "qq-registration.png"), 1240, 820);
            VerifyInputs(registration);
            registration.Close();
            var password = new CredentialDialog(accounts[0], new AccountCredentialService(repository));
            Render(password, Path.Combine(output, "account-password.png"), 650, 590);
            VerifyInputs(password);
            password.Close();
            redemptionEditor.Close();
            phoneEditor.Close();
            FormBehaviorChecks.Run(Path.Combine(data, "form-checks.db"));
            RegistrationFormChecks.Run(Path.Combine(data, "registration-form-checks"));
            editor.Close();
            sample.Close();
            empty.Close();
            Console.WriteLine("Rendered native WPF content offscreen; no desktop input or external service actions.");
            Console.WriteLine(output);
            app.Shutdown();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void VerifyPrimaryAction(MainWindow window)
    {
        if (window.FindName("QqRegisterButton") is not Button action) throw new Exception("Primary action missing.");
        var background = BrushColor(action.Background);
        CheckContrast(BrushColor(action.Foreground), background, "primary button");
        var labels = Descendants(action).OfType<TextBlock>().Where(x => x.Text.Contains("注册 QQ")).ToList();
        if (labels.Count == 0) throw new Exception("Primary action text not rendered.");
        foreach (var label in labels) CheckContrast(BrushColor(label.Foreground), background, "rendered primary text");
    }

    private static void VerifyInputs(Window editor)
    {
        foreach (var input in Descendants((DependencyObject)editor.Content).OfType<TextBox>())
        {
            if (input.FontSize < 13) throw new Exception("Input text is smaller than 13px.");
            CheckContrast(BrushColor(input.Foreground), BrushColor(input.Background), "input text");
        }
        foreach (var input in Descendants((DependencyObject)editor.Content).OfType<PasswordBox>())
        {
            if (input.FontSize < 13) throw new Exception("Password text is smaller than 13px.");
            CheckContrast(BrushColor(input.Foreground), BrushColor(input.Background), "password text");
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Color BrushColor(Brush brush) => brush is SolidColorBrush solid ? solid.Color : throw new Exception("Expected a solid readable surface.");
    private static void CheckContrast(Color foreground, Color background, string label)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte channel) { var value = channel / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }
        var a = Luminance(foreground);
        var b = Luminance(background);
        var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        if (ratio < 4.5) throw new Exception($"Insufficient {label} contrast: {ratio:F2}");
        Console.WriteLine($"CONTRAST {label}: {ratio:F2}:1");
    }

    private static void SetPreviewStatus(MainWindow window)
    {
        if (window.FindName("StatusText") is TextBlock status) status.Text = "视觉预览 · 使用独立示例数据，不是真实账号或兑换结果";
    }

    private static void Render(Window window, string path, int width, int height)
    {
        if (window.Content is not FrameworkElement content) throw new InvalidOperationException("Window has no visual content.");
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine("PREVIEW " + path);
    }
}
