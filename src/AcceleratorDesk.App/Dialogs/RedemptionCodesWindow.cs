using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using AcceleratorDesk.App.Services;

namespace AcceleratorDesk.App.Dialogs;

public sealed class RedemptionCodesWindow : Window
{
    private readonly RedemptionCodeStore _store;
    private readonly Action<string> _copy;
    private readonly StackPanel _rows = new();
    private readonly TextBox _newCode = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };

    public RedemptionCodesWindow(RedemptionCodeStore store, Action<string>? copy = null)
    {
        _store = store; _copy = copy ?? Clipboard.SetText;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AcceleratorAccountDesk;component/Theme.Dark.xaml", UriKind.Relative) });
        Style = (Style)FindResource(typeof(Window));
        Title = "兑换码 · 加速账户助手"; Width = 790; Height = 760; MinWidth = 650; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; WindowStyle = WindowStyle.None; ShowInTaskbar = false;
        UseLayoutRounding = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 42, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(42) }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var caption = new Grid { Background = (Brush)FindResource("Panel") };
        caption.Children.Add(new TextBlock { Text = "加速账户助手  /  兑换码", Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = (Brush)FindResource("Muted") });
        var close = new Button { Content = "\uE8BB", Style = (Style)FindResource("CaptionButton"), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "关闭兑换码窗口"); WindowChrome.SetIsHitTestVisibleInChrome(close, true); close.Click += (_, _) => Close(); caption.Children.Add(close); root.Children.Add(caption);
        var body = new Grid { Margin = new Thickness(24) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(body, 1); root.Children.Add(body);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "兑换码", FontSize = 25, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "复制后粘贴到迅雷客户端的「兑换码」窗口。是否有效、领取资格和到账时长，以官方结果为准。", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 8, 0, 0) }); body.Children.Add(heading);
        var list = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) }; Grid.SetRow(list, 1); body.Children.Add(list);
        var footer = new StackPanel { Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(footer, 2); body.Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "保存其他兑换码", Style = (Style)FindResource("FieldLabel") });
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new()); inputRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        AutomationProperties.SetName(_newCode, "新增兑换码"); inputRow.Children.Add(_newCode);
        var add = new Button { Content = "保存兑换码", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(10, 0, 0, 0) }; AutomationProperties.SetName(add, "保存兑换码"); Grid.SetColumn(add, 1); inputRow.Children.Add(add);
        add.Click += (_, _) => Run(() => { var added = _store.Add(_newCode.Text); Refresh(); _newCode.Clear(); _status.Text = added ? "已保存，可直接复制使用。" : "这个兑换码已经在列表中。"; }); footer.Children.Add(inputRow);
        _status.Margin = new Thickness(0, 10, 0, 0); _status.Style = (Style)FindResource("MutedText"); AutomationProperties.SetName(_status, "兑换码状态"); footer.Children.Add(_status);
        Content = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), Background = (Brush)FindResource("Bg"), Child = root };
        Run(Refresh);
    }

    private void Refresh()
    {
        var entries = _store.Load(); _rows.Children.Clear();
        foreach (var entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(155) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var code = new TextBlock { Text = entry.Code, FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            row.Children.Add(code);
            var note = new TextBlock { Text = entry.Note, Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 18, 0) }; Grid.SetColumn(note, 1); row.Children.Add(note);
            var copy = new Button { Content = "复制", MinWidth = 70, VerticalAlignment = VerticalAlignment.Center }; AutomationProperties.SetName(copy, "复制兑换码 " + entry.Code);
            copy.Click += (_, _) => Run(() => { _copy(entry.Code); _status.Text = "已复制：" + entry.Code + "。请在官方客户端核对兑换结果。"; }); Grid.SetColumn(copy, 2); row.Children.Add(copy);
            _rows.Children.Add(new Border { Background = (Brush)FindResource("Panel"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 9), Child = row });
        }
    }
    private void Run(Action action) { try { action(); } catch (Exception error) { _status.Text = "未完成：" + error.Message; } }
}
