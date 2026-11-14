using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcceleratorDesk.App.Dialogs;

public abstract class RecordDialog : Window
{
    private readonly TextBlock _error;
    protected ContentControl FormHost { get; }

    protected RecordDialog(string title, string subtitle, string saveLabel = "保存记录")
    {
        Title = title;
        Width = 720;
        Height = 680;
        MinWidth = 620;
        MinHeight = 560;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(25) };
        foreach (var length in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = length });
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = subtitle, Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 7, 0, 0) });
        root.Children.Add(header);
        FormHost = new ContentControl { VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(FormHost, 1);
        root.Children.Add(FormHost);
        _error = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(255, 166, 136)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10), MaxHeight = 72 };
        Grid.SetRow(_error, 2);
        root.Children.Add(_error);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = saveLabel, Style = (Style)FindResource("PrimaryButton"), MinWidth = 120, IsDefault = true };
        save.Click += (_, _) =>
        {
            try { SaveRecord(); DialogResult = true; }
            catch (Exception ex) { _error.Text = "未保存：" + ex.Message; }
        };
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;
    }

    protected abstract void SaveRecord();

    protected static TextBox Input(string value = "", bool multiline = false) => new()
    {
        Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 90 : 38, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden
    };

    protected static StackPanel Field(string label, Control input, string hint = "")
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        var text = new TextBlock { Text = label, Style = (Style)Application.Current.FindResource("FieldLabel") };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        panel.Children.Add(text);
        panel.Children.Add(input);
        if (hint.Length > 0) panel.Children.Add(new TextBlock { Text = hint, Style = (Style)Application.Current.FindResource("MutedText"), FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
        return panel;
    }

    protected static ScrollViewer Scroll(Panel body) => new() { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 15, 8, 0) };
    protected static ComboBox Select<T>(IEnumerable<Choice<T>> choices, T selected)
    {
        var items = choices.ToList();
        return new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, selected)) };
    }
    protected static T Selected<T>(ComboBox combo) => combo.SelectedItem is Choice<T> choice ? choice.Value : throw new ArgumentException("请选择一个有效选项。");
}

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
