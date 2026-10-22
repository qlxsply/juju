using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;
using Juju.Core.Tools;
using MediaBrushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Juju.App.Settings;

/// <summary>
/// 为全部设置页统一创建紧凑的圆角卡片和“文字左、控件右”的行。
/// 保留原生 WPF CheckBox/TextBox/ComboBox，键盘导航和无障碍语义不会因外观重做而丢失。
/// </summary>
internal static class SettingsViewFactory
{
    private static Brush Resource(string name) => (Brush)System.Windows.Application.Current.Resources[name];

    public static StackPanel Page()
    {
        // 页眉、英文 eyebrow 与说明在所有页面统一移除；导航已经明确当前页面。
        return new StackPanel { Margin = new Thickness(38, 16, 36, 30) };
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

    public static Action<string> Row(Border card, string title, string detail, FrameworkElement control)
        => Row((StackPanel)card.Child, title, detail, control);

    public static Action<string> Row(StackPanel rows, string title, string detail, FrameworkElement control)
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
        TextBlock? description = null;
        if (!string.IsNullOrWhiteSpace(detail))
        {
            description = new TextBlock
            {
                Text = detail, FontSize = 11, Foreground = Resource("ToolboxMuted"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 14, 0)
            };
            copy.Children.Add(description);
        }
        grid.Children.Add(copy);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        var row = new Border { BorderBrush = Resource("ToolboxLine"), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
        rows.Children.Add(row);
        return value => { if (description is not null) description.Text = value; };
    }

    public static CheckBox Switch(string accessibleName, bool value) => new()
    {
        IsChecked = value,
        Style = (Style)System.Windows.Application.Current.Resources["ToolboxSwitch"],
        ToolTip = accessibleName
    };

    public static TextBox Input(string value, double width = 168) => new()
    {
        Text = value, Width = width, MinHeight = 32,
        Style = (Style)System.Windows.Application.Current.Resources["ToolboxTextInput"],
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public static Button SoftButton(string text, RoutedEventHandler click) => new Button()
    {
        Content = text, Style = (Style)System.Windows.Application.Current.Resources["ToolboxSoftButton"],
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left
    }.Also(click);

    /// <summary>以按键标签显示绑定，并使用铅笔打开独立编辑弹窗，不把快捷键当普通文本输入。</summary>
    public static Action<string> ShortcutRow(Border card, string title, string detail, string shortcut, Action edit) =>
        ShortcutRow((StackPanel)card.Child, title, detail, shortcut, edit);

    /// <summary>创建统一的快捷键分组：分组标题、白色列表卡片，以及名称/按键/编辑按钮行。</summary>
    public static Dictionary<string, Action<string>> ShortcutGroup(StackPanel page, string heading,
        IEnumerable<(string Id, string Name, string Shortcut, Action Edit)> shortcuts)
    {
        var card = Group(page, heading);
        return shortcuts.ToDictionary(item => item.Id,
            item => ShortcutRow(card, item.Name, "", item.Shortcut, item.Edit), StringComparer.OrdinalIgnoreCase);
    }

    public static Action<string> ShortcutRow(StackPanel rows, string title, string detail, string shortcut, Action edit)
    {
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center };
        var keycaps = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center };
        void RenderKeys(string value)
        {
            keycaps.Children.Clear();
            if (value == GlobalHotkeyPolicy.None)
            {
                keycaps.Children.Add(new TextBlock { Text = "未设置", FontSize = 12, Foreground = Resource("ToolboxMuted") });
                return;
            }
            var strokes = value.Split(' ');
            for (var index = 0; index < strokes.Length; index++)
            {
                if (index > 0) keycaps.Children.Add(new TextBlock { Text = "→", FontSize = 11, Foreground = Resource("ToolboxMuted"),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 3, 0) });
                foreach (var key in strokes[index].Split('+')) keycaps.Children.Add(new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString("#EBF1FF")!,
                    BorderBrush = (Brush)new BrushConverter().ConvertFromString("#DCE7FF")!,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 3, 0),
                    Child = new TextBlock { Text = key, FontSize = 11,
                        Foreground = (Brush)new BrushConverter().ConvertFromString("#466EC9")! }
                });
            }
        }
        RenderKeys(shortcut);
        panel.Children.Add(keycaps);
        var editButton = new Button { Width = 36, Height = 36, ToolTip = "编辑快捷键",
            Style = (Style)System.Windows.Application.Current.Resources["ToolboxHeaderButton"],
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4,16.5 L3,21 L7.5,20 L20.5,7.5 L16.5,3.5 Z M14.5,5.5 L18.5,9.5"),
                Width = 18, Height = 18, Stretch = Stretch.Uniform, Stroke = Resource("ToolboxMuted"),
                StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round
            }};
        editButton.Click += (_, _) => edit();
        panel.Children.Add(editButton);
        Row(rows, title, detail, panel);
        return RenderKeys;
    }

    public static Action<string> DirectoryRow(Border card, string title, string path, Action edit, Action open)
    {
        var location = new TextBlock { Text = path, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = path, FontSize = 12, Foreground = Resource("ToolboxMuted"), VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Resource("ToolboxInk"), Margin = new Thickness(0, 0, 0, 5)
        });
        content.Children.Add(location);

        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var editButton = DirectoryAction("修改路径", "M4,16.5 L3,21 L7.5,20 L20.5,7.5 L16.5,3.5 Z M14.5,5.5 L18.5,9.5", edit);
        actions.Children.Add(editButton);
        var openButton = DirectoryAction("打开文件夹",
            "M7,2.5 L15,2.5 L19,6.5 L19,17.5 L7,17.5 Z M15,2.5 L15,6.5 L19,6.5 M4,6.5 L4,21.5 L16,21.5",
            open);
        openButton.Margin = new Thickness(4, 0, 0, 0);
        actions.Children.Add(openButton);

        var layout = new Grid { MinHeight = 80, Margin = new Thickness(20, 0, 16, 0) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folderIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M2.5,5.5 L9,5.5 L11,7.5 L21.5,7.5 L21.5,19.5 L2.5,19.5 Z"),
            Width = 20, Height = 20, Stretch = Stretch.Uniform, Stroke = Resource("ToolboxInk"),
            StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        layout.Children.Add(folderIcon);
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);
        Grid.SetColumn(actions, 2);
        layout.Children.Add(actions);
        if (card.Child is not StackPanel)
            throw new InvalidOperationException("目录设置必须位于设置卡片中。");
        card.CornerRadius = new CornerRadius(3);
        card.Child = layout;
        void UpdatePath(string value)
        {
            location.Text = value;
            location.ToolTip = value;
        }
        return UpdatePath;
    }

    private static Button DirectoryAction(string tooltip, string geometry, Action action)
    {
        var button = new Button
        {
            Width = 36, Height = 36, ToolTip = tooltip,
            Style = (Style)System.Windows.Application.Current.Resources["ToolboxHeaderButton"],
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(geometry), Width = 18, Height = 20, Stretch = Stretch.Uniform,
                Stroke = Resource("ToolboxInk"), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("无法打开数据目录：" + ex.Message, "Toolbox 设置",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static Button Also(this Button button, RoutedEventHandler click)
    {
        button.Click += click;
        return button;
    }

}
