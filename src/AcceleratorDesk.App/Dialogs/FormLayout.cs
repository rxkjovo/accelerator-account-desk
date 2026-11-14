using System.Windows;
using System.Windows.Controls;

namespace AcceleratorDesk.App.Dialogs;

internal static class FormLayout
{
    public static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        left.Margin = new Thickness(0, 0, 10, 15);
        right.Margin = new Thickness(10, 0, 0, 15);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
