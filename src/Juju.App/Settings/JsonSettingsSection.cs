using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Juju.Tools.Json.Settings;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace Juju.App.Settings;

// JSON 设置页以运行时创建的 WPF 控件实现，并通过 ISettingsSection 接口被 SettingsWindow 发现。
public sealed class JsonSettingsSection : ISettingsSection
{
    private readonly IJsonToolSettingsService _settings;
    private readonly System.Windows.Controls.TextBox _autosave = new();
    private readonly System.Windows.Controls.TextBox _indent = new();

    private readonly System.Windows.Controls.TextBox _filter = new()
        { Margin = new Thickness(0, 12, 0, 6), Padding = new Thickness(6), ToolTip = "按命令、来源、上下文或快捷键过滤" };

    private readonly List<ShortcutRow> _rows;
    private readonly StackPanel _shortcutRows = new();

    public string Id => "json";
    public string DisplayName => "JSON";
    public FrameworkElement View { get; } = new StackPanel { Margin = new Thickness(16) };

    // 设置服务由 DI 注入；控件只编辑内存中的行模型，点击总保存时才写入持久化配置。
    public JsonSettingsSection(IJsonToolSettingsService settings)
    {
        _settings = settings;
        var panel = (StackPanel)View;
        AddField(panel, "自动保存延迟（毫秒）", _autosave);
        AddField(panel, "缩进空格数", _indent);
        _autosave.Text = settings.Current.AutosaveDelayMilliseconds.ToString();
        _indent.Text = settings.Current.IndentSize.ToString();

        panel.Children.Add(new TextBlock
            { Margin = new Thickness(0, 20, 0, 0), FontSize = 16, FontWeight = FontWeights.SemiBold, Text = "快捷键" });
        panel.Children.Add(_filter);
        _rows = JsonToolShortcuts.Definitions
            .Select(definition => new ShortcutRow(definition, settings.Current.Shortcuts[definition.Command])).ToList();
        // TextChanged 是 WPF 事件；每次过滤重建可见行，但保留 _rows 中的编辑值。
        _filter.TextChanged += (_, _) => RefreshShortcutRows();
        panel.Children.Add(CreateShortcutTable());
    }

    // 目标设置是 record 风格的不可变值；对象初始化器仅替换 Shortcuts 属性。
    public Task SaveAsync() => _settings.SaveAsync(new(ReadNumber(_autosave, 100, 10000), ReadNumber(_indent, 1, 8))
    {
        Shortcuts = _rows.ToDictionary(row => row.Command, row => row.Shortcut)
    });

    private Border CreateShortcutTable()
    {
        var table = new Grid { MinHeight = 320 };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition());

        var header = CreateTableGrid(MediaBrushes.SteelBlue);
        AddHeaderCell(header, "命令", 0);
        AddHeaderCell(header, "来源", 1);
        AddHeaderCell(header, "上下文", 2);
        AddHeaderCell(header, "键绑定", 3);
        AddHeaderCell(header, "操作", 4);
        header.Margin = new Thickness(0, 0, SystemParameters.VerticalScrollBarWidth, 0);
        table.Children.Add(header);

        var rows = new ScrollViewer
        {
            Content = _shortcutRows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible
        };
        Grid.SetRow(rows, 1);
        table.Children.Add(rows);

        RefreshShortcutRows();
        return new Border
            { BorderBrush = MediaBrushes.LightSteelBlue, BorderThickness = new Thickness(1), Child = table };
    }

    // 这里重建的是视觉行而不是数据模型，所以过滤和重置不会丢失未保存的 ShortcutRow。
    private void RefreshShortcutRows()
    {
        _shortcutRows.Children.Clear();
        var rowIndex = 0;
        foreach (var row in _rows.Where(MatchesFilter))
        {
            var grid = CreateTableGrid(rowIndex++ % 2 == 0 ? MediaBrushes.White : MediaBrushes.AliceBlue);
            AddTextCell(grid, row.CommandName, 0);
            AddTextCell(grid, row.Source, 1);
            AddTextCell(grid, row.Context, 2);

            var shortcut = new System.Windows.Controls.TextBox { MinHeight = 26, Text = row.Shortcut };
            // 事件直接同步行模型；INotifyPropertyChanged 则供任何绑定消费者观察后续变化。
            shortcut.TextChanged += (_, _) => row.Shortcut = shortcut.Text;
            AddCell(grid, shortcut, 3);

            var reset = new System.Windows.Controls.Button { Content = "重置", Padding = new Thickness(8, 2, 8, 2) };
            reset.Click += (_, _) =>
            {
                row.Shortcut = row.Default;
                shortcut.Text = row.Shortcut;
            };
            AddCell(grid, reset, 4);
            _shortcutRows.Children.Add(grid);
        }
    }

    private static Grid CreateTableGrid(MediaBrush background)
    {
        var grid = new Grid { Background = background, MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        return grid;
    }

    private static void AddHeaderCell(Grid grid, string text, int column)
    {
        AddCell(grid,
            new TextBlock
            {
                FontWeight = FontWeights.SemiBold, Foreground = MediaBrushes.White, Text = text,
                VerticalAlignment = VerticalAlignment.Center
            }, column);
    }

    private static void AddTextCell(Grid grid, string text, int column)
    {
        AddCell(grid,
            new TextBlock
            {
                Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            }, column);
    }

    private static void AddCell(Grid grid, UIElement content, int column)
    {
        var cell = new Border
        {
            BorderBrush = MediaBrushes.LightSteelBlue, BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 6, 8, 6), Child = content
        };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private bool MatchesFilter(ShortcutRow row)
    {
        var filter = _filter.Text.Trim();
        return string.IsNullOrEmpty(filter) ||
               $"{row.CommandName} {row.Source} {row.Context} {row.Shortcut}".Contains(filter,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void AddField(System.Windows.Controls.Panel panel, string label,
        System.Windows.Controls.Control control)
    {
        panel.Children.Add(new TextBlock { Margin = new Thickness(0, 14, 0, 3), Text = label });
        panel.Children.Add(control);
    }

    private static int ReadNumber(System.Windows.Controls.TextBox box, int minimum, int maximum) =>
        int.TryParse(box.Text, out var value) && value >= minimum && value <= maximum
            ? value
            : throw new InvalidOperationException($"{box.Text} 不是有效范围内的数字。");

    // C# 主构造函数把只用于初始化的参数放在类型声明处；Java 中可理解为简化的构造函数和字段赋值。
    private sealed class ShortcutRow(JsonToolCommandMetadata definition, string shortcut) : INotifyPropertyChanged
    {
        private string _shortcut = shortcut;
        public JsonToolCommand Command => definition.Command;
        public string CommandName => definition.DisplayName;
        public string Source => definition.Source == JsonToolShortcutSource.Monaco ? "Monaco" : "Juju";

        public string Context => definition.Context switch
        {
            JsonToolShortcutContext.Editor => "编辑器", JsonToolShortcutContext.List => "文件列表", _ => "对比"
        };

        public string Default => definition.Default;

        public string Shortcut
        {
            get => _shortcut;
            set
            {
                if (_shortcut == value) return;
                _shortcut = value;
                // 标准 .NET 属性变更通知，作用类似 JavaBeans PropertyChangeSupport。
                PropertyChanged?.Invoke(this, new(nameof(Shortcut)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}