using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Image = System.Windows.Controls.Image;

namespace Juju.App.Settings;

/// <summary>
/// 把原型的 eyebrow/标题/说明、圆角设置卡片和“文字左、控件右”的行统一创建。
/// 保留原生 WPF CheckBox/TextBox/ComboBox，键盘导航和无障碍语义不会因外观重做而丢失。
/// </summary>
internal static class SettingsViewFactory
{
    private static Brush Resource(string name) => (Brush)System.Windows.Application.Current.Resources[name];

    public static StackPanel Page(string eyebrow, string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(38, 30, 36, 36) };
        panel.Children.Add(new TextBlock
        {
            Text = eyebrow, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString("#8B9BCC")!
        });
        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = Resource("ToolboxInk"), Margin = new Thickness(0, 11, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = description, FontSize = 12, Foreground = Resource("ToolboxMuted"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 16),
            LineHeight = 21
        });
        return panel;
    }

    public static Border Group(StackPanel page, string heading)
    {
        page.Children.Add(new TextBlock
        {
            Text = heading, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = Resource("ToolboxInk"), Margin = new Thickness(0, 21, 0, 11)
        });
        var card = new Border { Style = (Style)System.Windows.Application.Current.Resources["ToolboxSettingsCard"] };
        card.Child = new StackPanel();
        page.Children.Add(card);
        return card;
    }

    /// <summary>工具详情页顶部的色块、名称、简介和“打开工具”按钮，复现原型 tool-profile。</summary>
    public static void Profile(StackPanel page, string title, string description, ImageSource icon,
        string iconBackground, RoutedEventHandler open)
    {
        var grid = new Grid { Margin = new Thickness(13) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(13),
            Background = (Brush)new BrushConverter().ConvertFromString(iconBackground)!,
            Child = new Image { Source = icon, Width = 23, Height = 23 }
        };
        grid.Children.Add(badge);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Resource("ToolboxInk") });
        copy.Children.Add(new TextBlock { Text = description, FontSize = 11,
            Foreground = Resource("ToolboxMuted"), Margin = new Thickness(0, 5, 0, 0) });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var button = SoftButton("打开工具", open);
        Grid.SetColumn(button, 2);
        button.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(button);
        page.Children.Add(new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#F9FAFF")!,
            BorderBrush = Resource("ToolboxLine"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Child = grid
        });
    }

    public static void Row(Border card, string title, string detail, FrameworkElement control)
    {
        var grid = new Grid { MinHeight = 50, Margin = new Thickness(16, 7, 16, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Resource("ToolboxInk"), TextWrapping = TextWrapping.Wrap
        });
        copy.Children.Add(new TextBlock
        {
            Text = detail, FontSize = 11, Foreground = Resource("ToolboxMuted"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 14, 0)
        });
        grid.Children.Add(copy);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        var row = new Border { BorderBrush = Resource("ToolboxLine"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        ((StackPanel)card.Child).Children.Add(row);
    }

    public static CheckBox Switch(string accessibleName, bool value) => new()
    {
        IsChecked = value,
        Style = (Style)System.Windows.Application.Current.Resources["ToolboxSwitch"],
        ToolTip = accessibleName
    };

    public static TextBox Input(string value, double width = 168) => new()
    {
        Text = value, Width = width, MinHeight = 32, Padding = new Thickness(8, 5, 8, 5),
        Background = (Brush)new BrushConverter().ConvertFromString("#FBFCFF")!,
        BorderBrush = Resource("ToolboxLine"), VerticalContentAlignment = VerticalAlignment.Center,
        FontSize = 12
    };

    public static Button SoftButton(string text, RoutedEventHandler click) => new Button()
    {
        Content = text, Style = (Style)System.Windows.Application.Current.Resources["ToolboxSoftButton"],
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left
    }.Also(click);

    private static Button Also(this Button button, RoutedEventHandler click)
    {
        button.Click += click;
        return button;
    }

    public static void Note(StackPanel page, string text)
    {
        page.Children.Add(new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#F9FAFF")!,
            BorderBrush = Resource("ToolboxLine"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(14),
            Child = new TextBlock { Text = text, FontSize = 11, LineHeight = 19,
                TextWrapping = TextWrapping.Wrap, Foreground = Resource("ToolboxMuted") }
        });
    }
}
