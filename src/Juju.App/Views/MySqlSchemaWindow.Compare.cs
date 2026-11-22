using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Juju.App.Resources;
using Juju.Tools.MySqlSchema.Application;
using Juju.Tools.MySqlSchema.Domain;
using Microsoft.Win32;
using MySqlDataGrid = System.Windows.Controls.DataGrid;
using MySqlButton = System.Windows.Controls.Button;

namespace Juju.App.Views;

public partial class MySqlSchemaWindow
{
    private readonly string[] _comparisonCategories =
        ["概览", "数据库", "字段", "主键", "索引", "外键", "检查约束", "分区", "选项", "视图"];
    private readonly CompareSource?[] _compareSources = new CompareSource?[2];
    private TextBlock?[] _compareSourceLabels = new TextBlock?[2];
    private CheckBox? _onlyDifferences;
    private ListBox? _categoryList;
    private MySqlDataGrid? _differenceGrid;
    private TextBlock? _compareSummary;
    private SchemaComparisonResult? _comparison;

    private FrameworkElement BuildComparePage()
    {
        var root = PageShell("结构对比", "比较两个历史结构快照或实时数据库对象；此工具只生成建议，不执行数据库变更。",
            out var content);
        var sources = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        sources.ColumnDefinitions.Add(new ColumnDefinition());
        sources.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        sources.ColumnDefinitions.Add(new ColumnDefinition());
        for ( var i = 0; i < 2; i++ )
        {
            var sideIndex = i;
            var card = new Border
            {
                Style = AppResources.GetRequired<Style>("ToolboxSettingsCard"),
                Padding = new Thickness(13)
            };
            Grid.SetColumn(card, i == 0 ? 0 : 2);
            var panel = new StackPanel();
            var header = new DockPanel();
            var label = new TextBlock
            {
                Text = i == 0 ? "A · 对比来源" : "B · 对比来源",
                FontWeight = FontWeights.SemiBold,
                Foreground = AppResources.GetRequired<Brush>("ToolboxInk"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            header.Children.Add(label);
            var configure = ActionButton("配置", () => _ = ConfigureCompareSourceAsync(sideIndex), primary: true);
            configure.Margin = new Thickness(0);
            DockPanel.SetDock(configure, Dock.Right);
            header.Children.Add(configure);
            panel.Children.Add(header);
            _compareSourceLabels[i] = new TextBlock
            {
                Text = "尚未选择来源",
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = "尚未选择来源"
            };
            panel.Children.Add(_compareSourceLabels[i]);
            card.Child = panel;
            sources.Children.Add(card);
        }
        content.Children.Add(sources);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(ActionButton("对比", () => _ = RunComparisonAsync(), primary: true));
        toolbar.Children.Add(ActionButton("⇄ 交换", SwapCompareSources));
        _onlyDifferences = new CheckBox
        {
            Content = "仅差异",
            IsChecked = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            Foreground = AppResources.GetRequired<Brush>("ToolboxInk")
        };
        _onlyDifferences.Checked += (_, _) => RenderComparison();
        _onlyDifferences.Unchecked += (_, _) => RenderComparison();
        toolbar.Children.Add(_onlyDifferences);
        _compareSummary = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            Margin = new Thickness(4, 0, 10, 0)
        };
        toolbar.Children.Add(_compareSummary);
        toolbar.Children.Add(ActionButton("元数据 A", () => ShowMetadata(0)));
        toolbar.Children.Add(ActionButton("元数据 B", () => ShowMetadata(1)));
        toolbar.Children.Add(ActionButton("原始 DDL / 变更 SQL", ShowMigrationSql));
        content.Children.Add(toolbar);

        var resultGrid = new Grid { MinHeight = 400 };
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _categoryList = new ListBox
        {
            BorderBrush = AppResources.GetRequired<Brush>("ToolboxLine"),
            Background = Brushes.White,
            ItemsSource = _comparisonCategories,
            SelectedIndex = 0
        };
        _categoryList.SelectionChanged += (_, _) => RenderComparison();
        Grid.SetColumn(_categoryList, 0);
        resultGrid.Children.Add(Card(_categoryList));
        _differenceGrid = new MySqlDataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brushes.White,
            BorderBrush = AppResources.GetRequired<Brush>("ToolboxLine"),
            BorderThickness = new Thickness(1),
            RowHeight = 46
        };
        _differenceGrid.Columns.Add(TextColumn("类别", nameof(DifferenceRow.Category), 0.8));
        _differenceGrid.Columns.Add(TextColumn("对象 / 属性组", nameof(DifferenceRow.Key), 1.2));
        _differenceGrid.Columns.Add(TextColumn("状态", nameof(DifferenceRow.Status), 0.8));
        _differenceGrid.Columns.Add(TextColumn("A", nameof(DifferenceRow.ValueA), 2));
        _differenceGrid.Columns.Add(TextColumn("B", nameof(DifferenceRow.ValueB), 2));
        _differenceGrid.MouseDoubleClick += (_, _) => ShowDifferenceDetails();
        Grid.SetColumn(_differenceGrid, 2);
        resultGrid.Children.Add(Card(_differenceGrid));
        content.Children.Add(resultGrid);
        _compareSummary.Text = "请选择 A、B 来源后执行对比。";
        return root;
    }

    private async Task ConfigureCompareSourceAsync(int index)
    {
        try
        {
            var dialog = new CompareSourceWindow(_workspace, _compareSources[index]) { Owner = this };
            if ( dialog.ShowDialog() != true || dialog.Source is null )
                return;
            _compareSources[index] = dialog.Source;
            var label = _compareSourceLabels[index];
            if ( label is not null )
            {
                label.Text = dialog.Source.DisplayName;
                label.ToolTip = dialog.Source.DisplayName;
            }
            _comparison = null;
            RenderComparison();
            SetStatus("对比来源已更改；请重新执行对比。", index == 0 ? "来源 A" : "来源 B");
        }
        catch ( Exception error ) { SetStatus(error.Message, "配置对比来源失败"); }
    }

    private async Task RunComparisonAsync()
    {
        if ( _compareSources[0] is not { } a || _compareSources[1] is not { } b )
        {
            SetStatus("请分别配置对比来源 A 和 B。", "结构对比");
            return;
        }
        try
        {
            SetStatus("正在读取来源结构并计算差异…", "结构对比");
            var schemaA = await ReadSourceAsync(a);
            var schemaB = await ReadSourceAsync(b);
            _comparison = MySqlSchemaWorkspace.Compare(schemaA, schemaB);
            RenderComparison();
            SetStatus($"对比完成：缺失 {_comparison.MissingCount} 项，修改 {_comparison.ModifiedCount} 项，属性变化 {_comparison.ChangedPropertyCount} 项。",
                "结构对比");
        }
        catch ( Exception error )
        {
            _comparison = null;
            RenderComparison();
            SetStatus("对比失败：" + error.Message, "结构对比");
        }
    }

    private Task<SchemaObject> ReadSourceAsync(CompareSource source) => source switch
    {
        SnapshotCompareSource snapshot => ReadSnapshotObjectAsync(snapshot),
        LiveCompareSource live => _workspace.ReadLiveObjectAsync(live.ConnectionId, live.DatabaseName, live.Object),
        _ => throw new InvalidOperationException("未知的结构来源类型。")
    };

    private async Task<SchemaObject> ReadSnapshotObjectAsync(SnapshotCompareSource source) =>
        (await _workspace.ReadSnapshotAsync(source.SnapshotId)).Object;

    private void SwapCompareSources()
    {
        (_compareSources[0], _compareSources[1]) = (_compareSources[1], _compareSources[0]);
        for ( var index = 0; index < 2; index++ )
        {
            var label = _compareSourceLabels[index];
            if ( label is null ) continue;
            label.Text = _compareSources[index]?.DisplayName ?? "尚未选择来源";
            label.ToolTip = label.Text;
        }
        _comparison = null;
        RenderComparison();
        SetStatus("A、B 来源已交换；请重新执行对比。", "结构对比");
    }

    private void RenderComparison()
    {
        if ( _differenceGrid is null || _categoryList is null ) return;
        var category = _categoryList.SelectedItem as string ?? "概览";
        var onlyDifferences = _onlyDifferences?.IsChecked == true;
        if ( _comparison is null )
        {
            _differenceGrid.ItemsSource = null;
            _compareSummary!.Text = "请选择 A、B 来源后执行对比。";
            return;
        }
        _compareSummary!.Text = $"缺失 {_comparison.MissingCount} · 修改 {_comparison.ModifiedCount} · 变化属性 {_comparison.ChangedPropertyCount}";
        var rows = _comparison.Differences.Select(item => ToDifferenceRow(item, category))
            .Where(item => item is not null).Select(item => item!).ToArray();
        if ( category == "概览" )
        {
            var all = _comparison.Differences.GroupBy(item => item.Category)
                .Select(group => new DifferenceRow(group.Key, "", $"{group.Count(item => item.Kind != SchemaDifferenceKind.Same)} 项差异",
                    $"{group.Count()} 个对象", ""))
                .ToList();
            all.Insert(0, new DifferenceRow("总计", "", $"{_comparison.MissingCount} 缺失 / {_comparison.ModifiedCount} 修改",
                $"{_comparison.Differences.Count} 个对比对象", $"变化属性 {_comparison.ChangedPropertyCount}"));
            _differenceGrid.ItemsSource = onlyDifferences
                ? all.Where(row => row.Category == "总计" || !row.Status.StartsWith("0 ", StringComparison.Ordinal)).ToArray()
                : all;
        }
        else
            _differenceGrid.ItemsSource = onlyDifferences
                ? rows.Where(row => row.Status != "相同").ToArray()
                : rows;
    }

    private static DifferenceRow? ToDifferenceRow(ObjectDifference difference, string selectedCategory)
    {
        if ( selectedCategory != difference.Category )
            return null;
        var changed = difference.Properties.Where(property => property.Changed).ToArray();
        var valueA = changed.Length == 0 ? "—" : string.Join("; ", changed.Select(property =>
            $"{property.Property}: {FormatValue(property.ValueA)}"));
        var valueB = changed.Length == 0 ? "—" : string.Join("; ", changed.Select(property =>
            $"{property.Property}: {FormatValue(property.ValueB)}"));
        var status = difference.Kind switch
        {
            SchemaDifferenceKind.Same => "相同",
            SchemaDifferenceKind.MissingInA => "A 缺失",
            SchemaDifferenceKind.MissingInB => "B 缺失",
            _ => $"修改（{changed.Length} 项属性）"
        };
        return new DifferenceRow(difference.Category, difference.Key, status, valueA, valueB, difference);
    }

    private static string FormatValue(string? value) => value switch
    {
        null => "NULL / 未提供",
        "" => "空字符串",
        _ => value
    };

    private void ShowDifferenceDetails()
    {
        if ( _differenceGrid?.SelectedItem is not DifferenceRow { Difference: { } difference } row )
            return;
        var dialog = new Window
        {
            Title = $"{row.Category} · {row.Key} 属性对比",
            Width = 780,
            Height = 620,
            MinWidth = 650,
            MinHeight = 480,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
        var grid = new MySqlDataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = difference.Properties.Select(property => new DifferencePropertyRow(property.Property,
                FormatValue(property.ValueA), FormatValue(property.ValueB), property.Changed ? "变化" : "相同")).ToArray()
        };
        grid.Columns.Add(TextColumn("属性", nameof(DifferencePropertyRow.Property), 1.2));
        grid.Columns.Add(TextColumn("A", nameof(DifferencePropertyRow.A), 2));
        grid.Columns.Add(TextColumn("B", nameof(DifferencePropertyRow.B), 2));
        grid.Columns.Add(TextColumn("状态", nameof(DifferencePropertyRow.Status), 0.8));
        dialog.Content = new Border { Padding = new Thickness(16), Child = grid };
        dialog.ShowDialog();
    }

    private async void ShowMetadata(int sideIndex)
    {
        if ( _compareSources[sideIndex] is not { } source )
        {
            SetStatus("请先配置该侧对比来源。", sideIndex == 0 ? "来源 A" : "来源 B");
            return;
        }
        try
        {
            var schema = await ReadSourceAsync(source);
            var dialog = TextPreviewWindow("元数据 · " + (sideIndex == 0 ? "A" : "B"),
                JsonSerializer.Serialize(schema, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            dialog.Owner = this;
            dialog.ShowDialog();
        }
        catch ( Exception error ) { SetStatus("读取元数据失败：" + error.Message, "结构对比"); }
    }

    private async void ShowMigrationSql()
    {
        if ( _comparison is null )
        {
            SetStatus("请先成功执行对比，再查看 DDL / 变更 SQL。", "结构对比");
            return;
        }
        var dialog = new MigrationSqlWindow(_comparison) { Owner = this };
        dialog.ShowDialog();
    }

    private static Window TextPreviewWindow(string title, string text)
    {
        var window = new Window
        {
            Title = title,
            Width = 900,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.White
        };
        window.Content = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(14)
        };
        return window;
    }

    private abstract record CompareSource(string DisplayName);
    private sealed record SnapshotCompareSource(string SnapshotId, string DisplayName) : CompareSource(DisplayName);
    private sealed record LiveCompareSource(string ConnectionId, string DatabaseName, DatabaseObjectInfo Object,
        string DisplayName) : CompareSource(DisplayName);
    private sealed record DifferenceRow(string Category, string Key, string Status, string ValueA, string ValueB,
        ObjectDifference? Difference = null);
    private sealed record DifferencePropertyRow(string Property, string A, string B, string Status);

    private sealed class CompareSourceWindow : Window
    {
        private readonly MySqlSchemaWorkspace _workspace;
        private readonly CompareSource? _initial;
        private readonly ComboBox _sourceType = new();
        private readonly ComboBox _environment = new();
        private readonly ComboBox _version = new();
        private readonly ComboBox _snapshotConnection = new();
        private readonly ComboBox _snapshotDatabase = new();
        private readonly ComboBox _snapshotObject = new();
        private readonly ComboBox _liveConnection = new();
        private readonly ComboBox _liveDatabase = new();
        private readonly ComboBox _liveKind = new();
        private readonly ComboBox _liveObject = new();
        private readonly StackPanel _snapshotPanel = new();
        private readonly StackPanel _livePanel = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
        private BackupTree _tree = new([]);
        private IReadOnlyList<MySqlConnectionProfile> _profiles = [];
        private bool _updating;

        public CompareSource? Source { get; private set; }

        public CompareSourceWindow(MySqlSchemaWorkspace workspace, CompareSource? initial)
        {
            _workspace = workspace;
            _initial = initial;
            Title = "配置结构对比来源";
            Width = 560;
            Height = 660;
            MinWidth = 500;
            MinHeight = 560;
            Owner = Application.Current?.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sourceLabel = new TextBlock { Text = "来源类型", Foreground = AppResources.GetRequired<Brush>("ToolboxMuted") };
            var typePanel = new StackPanel();
            typePanel.Children.Add(sourceLabel);
            _sourceType.ItemsSource = new[] { "历史结构快照", "实时数据库" };
            _sourceType.SelectedIndex = initial is LiveCompareSource ? 1 : 0;
            _sourceType.Height = 34;
            _sourceType.Margin = new Thickness(0, 4, 0, 10);
            _sourceType.SelectionChanged += async (_, _) => await SourceTypeChangedAsync();
            typePanel.Children.Add(_sourceType);
            Grid.SetRow(typePanel, 0);
            root.Children.Add(typePanel);

            var panels = new StackPanel();
            AddHistoryPanel(panels);
            AddLivePanel(panels);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panels };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
            Grid.SetRow(_status, 2);
            _status.Margin = new Thickness(0, 8, 0, 0);
            root.Children.Add(_status);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new MySqlButton { Content = "取消", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
            cancel.Click += (_, _) => DialogResult = false;
            var accept = new MySqlButton { Content = "确定", Style = AppResources.GetRequired<Style>("ToolboxSoftButton") };
            accept.Margin = new Thickness(8, 0, 0, 0);
            accept.Click += (_, _) => AcceptSelection();
            footer.Children.Add(cancel);
            footer.Children.Add(accept);
            Grid.SetRow(footer, 3);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(footer);
            Content = root;

            _environment.SelectionChanged += async (_, _) => await EnvironmentSelectionChangedAsync();
            _version.SelectionChanged += (_, _) => { if ( !_updating ) LoadSnapshotConnections(); };
            _snapshotConnection.SelectionChanged += (_, _) => { if ( !_updating ) LoadSnapshotDatabases(); };
            _snapshotDatabase.SelectionChanged += (_, _) => { if ( !_updating ) LoadSnapshotDocuments(); };
            _liveConnection.SelectionChanged += async (_, _) => await LiveConnectionChangedAsync();
            _liveDatabase.SelectionChanged += async (_, _) => await LiveDatabaseChangedAsync();
            _liveKind.SelectionChanged += async (_, _) => await LiveDatabaseChangedAsync();
            Loaded += async (_, _) => await InitializeAsync();
        }

        private void AddHistoryPanel(Panel host)
        {
            AddField(_snapshotPanel, "环境", _environment);
            AddField(_snapshotPanel, "版本", _version);
            AddField(_snapshotPanel, "连接", _snapshotConnection);
            AddField(_snapshotPanel, "数据库", _snapshotDatabase);
            AddField(_snapshotPanel, "备份对象", _snapshotObject);
            host.Children.Add(_snapshotPanel);
        }

        private void AddLivePanel(Panel host)
        {
            AddField(_livePanel, "连接", _liveConnection);
            AddField(_livePanel, "数据库", _liveDatabase);
            _liveKind.ItemsSource = new[] { "表", "视图" };
            _liveKind.SelectedIndex = 0;
            AddField(_livePanel, "对象类型", _liveKind);
            AddField(_livePanel, "对象", _liveObject);
            host.Children.Add(_livePanel);
        }

        private static void AddField(Panel panel, string label, ComboBox combo)
        {
            combo.Height = 34;
            combo.Margin = new Thickness(0, 4, 0, 10);
            combo.VerticalContentAlignment = VerticalAlignment.Center;
            panel.Children.Add(new TextBlock { Text = label, Foreground = AppResources.GetRequired<Brush>("ToolboxMuted") });
            panel.Children.Add(combo);
        }

        private async Task InitializeAsync()
        {
            try
            {
                _updating = true;
                _tree = await _workspace.ReadBackupTreeAsync();
                _profiles = await _workspace.ListConnectionsAsync();
                _environment.ItemsSource = _tree.Environments;
                _environment.DisplayMemberPath = nameof(EnvironmentNode.Name);
                _liveConnection.ItemsSource = _profiles;
                _liveConnection.DisplayMemberPath = nameof(MySqlConnectionProfile.Name);
                var initialSnapshot = _initial as SnapshotCompareSource;
                var initialLive = _initial as LiveCompareSource;
                if ( initialSnapshot is not null )
                    SelectSnapshotPath(initialSnapshot.SnapshotId);
                if ( initialLive is not null )
                    _liveConnection.SelectedItem = _profiles.SingleOrDefault(profile => profile.Id == initialLive.ConnectionId);
                _updating = false;
                ToggleSourcePanels();
                if ( _sourceType.SelectedIndex == 0 )
                    await EnvironmentSelectionChangedAsync();
                else
                    await LiveConnectionChangedAsync();
            }
            catch ( Exception error )
            { _status.Text = "加载来源失败：" + error.Message; }
            finally { _updating = false; }
        }

        private void SelectSnapshotPath(string snapshotId)
        {
            var environment = _tree.Environments.FirstOrDefault(item => item.Versions.Any(version =>
                version.Connections.Any(connection => connection.Databases.Any(database =>
                    database.Documents.Any(document => document.Id == snapshotId)))));
            if ( environment is null ) return;
            _environment.SelectedItem = environment;
            var version = environment.Versions.First(item => item.Connections.Any(connection =>
                connection.Databases.Any(database => database.Documents.Any(document => document.Id == snapshotId))));
            _version.SelectedItem = version;
            var connection = version.Connections.First(item => item.Databases.Any(database =>
                database.Documents.Any(document => document.Id == snapshotId)));
            _snapshotConnection.ItemsSource = version.Connections;
            _snapshotConnection.DisplayMemberPath = nameof(ConnectionNode.NameAtFirstBackup);
            _snapshotConnection.SelectedItem = connection;
            var database = connection.Databases.First(item => item.Documents.Any(document => document.Id == snapshotId));
            _snapshotDatabase.ItemsSource = connection.Databases;
            _snapshotDatabase.DisplayMemberPath = nameof(DatabaseNode.Name);
            _snapshotDatabase.SelectedItem = database;
            _snapshotObject.ItemsSource = database.Documents;
            _snapshotObject.DisplayMemberPath = nameof(BackupDocumentRef.Name);
            _snapshotObject.SelectedItem = database.Documents.Single(document => document.Id == snapshotId);
        }

        private async Task SourceTypeChangedAsync()
        {
            if ( _updating ) return;
            ToggleSourcePanels();
            if ( _sourceType.SelectedIndex == 0 )
                await EnvironmentSelectionChangedAsync();
            else
                await LiveConnectionChangedAsync();
        }

        private void ToggleSourcePanels()
        {
            var history = _sourceType.SelectedIndex == 0;
            _snapshotPanel.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
            _livePanel.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        }

        private async Task EnvironmentSelectionChangedAsync()
        {
            if ( _updating ) return;
            _updating = true;
            try
            {
                var environment = _environment.SelectedItem as EnvironmentNode;
                _version.ItemsSource = environment?.Versions ?? [];
                _version.DisplayMemberPath = nameof(VersionNode.Name);
                if ( _initial is SnapshotCompareSource initial && _environment.SelectedItem is EnvironmentNode env )
                    _version.SelectedItem = env.Versions.FirstOrDefault(item => item.Connections.Any(connection =>
                        connection.Databases.Any(database => database.Documents.Any(doc => doc.Id == initial.SnapshotId))));
                if ( _version.SelectedItem is null ) _version.SelectedItem = environment?.Versions.FirstOrDefault();
                LoadSnapshotConnections();
            }
            finally { _updating = false; }
        }

        private void LoadSnapshotConnections()
        {
            var version = _version.SelectedItem as VersionNode;
            _snapshotConnection.ItemsSource = version?.Connections ?? [];
            _snapshotConnection.DisplayMemberPath = nameof(ConnectionNode.NameAtFirstBackup);
            if ( _initial is SnapshotCompareSource initial && version is not null )
                _snapshotConnection.SelectedItem = version.Connections.FirstOrDefault(item => item.Databases.Any(database =>
                    database.Documents.Any(doc => doc.Id == initial.SnapshotId)));
            if ( _snapshotConnection.SelectedItem is null ) _snapshotConnection.SelectedItem = version?.Connections.FirstOrDefault();
            LoadSnapshotDatabases();
        }

        private void LoadSnapshotDatabases()
        {
            var connection = _snapshotConnection.SelectedItem as ConnectionNode;
            _snapshotDatabase.ItemsSource = connection?.Databases ?? [];
            _snapshotDatabase.DisplayMemberPath = nameof(DatabaseNode.Name);
            if ( _initial is SnapshotCompareSource initial && connection is not null )
                _snapshotDatabase.SelectedItem = connection.Databases.FirstOrDefault(item =>
                    item.Documents.Any(doc => doc.Id == initial.SnapshotId));
            if ( _snapshotDatabase.SelectedItem is null ) _snapshotDatabase.SelectedItem = connection?.Databases.FirstOrDefault();
            LoadSnapshotDocuments();
        }

        private void LoadSnapshotDocuments()
        {
            var database = _snapshotDatabase.SelectedItem as DatabaseNode;
            _snapshotObject.ItemsSource = database?.Documents ?? [];
            _snapshotObject.DisplayMemberPath = nameof(BackupDocumentRef.Name);
            if ( _initial is SnapshotCompareSource initial && database is not null )
                _snapshotObject.SelectedItem = database.Documents.FirstOrDefault(item => item.Id == initial.SnapshotId);
            if ( _snapshotObject.SelectedItem is null ) _snapshotObject.SelectedItem = database?.Documents.FirstOrDefault();
        }

        private async Task LiveConnectionChangedAsync()
        {
            if ( _updating ) return;
            _updating = true;
            try
            {
                var profile = _liveConnection.SelectedItem as MySqlConnectionProfile;
                var databases = profile is null ? [] : await _workspace.ListDatabasesAsync(profile.Id);
                _liveDatabase.ItemsSource = databases;
                _liveDatabase.DisplayMemberPath = nameof(DatabaseInfo.Name);
                if ( _initial is LiveCompareSource initial && profile?.Id == initial.ConnectionId )
                    _liveDatabase.SelectedItem = databases.FirstOrDefault(item => item.Name == initial.DatabaseName);
                if ( _liveDatabase.SelectedItem is null )
                    _liveDatabase.SelectedItem = databases.FirstOrDefault(item => item.Name == profile?.DefaultDatabase)
                        ?? databases.FirstOrDefault();
                _liveKind.SelectedIndex = _initial is LiveCompareSource live && live.Object.Kind == SchemaObjectKind.View ? 1 : 0;
            }
            catch ( Exception error )
            {
                _liveDatabase.ItemsSource = null;
                _status.Text = "读取实时数据库失败：" + error.Message;
            }
            finally { _updating = false; }
            await LiveDatabaseChangedAsync();
        }

        private async Task LiveDatabaseChangedAsync()
        {
            if ( _updating ) return;
            _updating = true;
            try
            {
                var profile = _liveConnection.SelectedItem as MySqlConnectionProfile;
                var database = _liveDatabase.SelectedItem as DatabaseInfo;
                var objects = profile is null || database is null
                    ? []
                    : await _workspace.ListObjectsAsync(profile.Id, database.Name);
                var kind = _liveKind.SelectedIndex == 1 ? SchemaObjectKind.View : SchemaObjectKind.Table;
                var typed = objects.Where(item => item.Kind == kind).ToArray();
                _liveObject.ItemsSource = typed;
                _liveObject.DisplayMemberPath = nameof(DatabaseObjectInfo.Name);
                if ( _initial is LiveCompareSource initial && profile?.Id == initial.ConnectionId &&
                     database?.Name == initial.DatabaseName )
                    _liveObject.SelectedItem = typed.FirstOrDefault(item => item.Name == initial.Object.Name);
                if ( _liveObject.SelectedItem is null ) _liveObject.SelectedItem = typed.FirstOrDefault();
                _status.Text = typed.Length == 0 ? "当前数据库中没有此类型的表/视图。" : "选择对象后确认。";
            }
            catch ( Exception error )
            {
                _liveObject.ItemsSource = null;
                _status.Text = "读取实时对象失败：" + error.Message;
            }
            finally { _updating = false; }
        }

        private void AcceptSelection()
        {
            if ( _sourceType.SelectedIndex == 0 )
            {
                if ( _environment.SelectedItem is not EnvironmentNode environment ||
                     _version.SelectedItem is not VersionNode version ||
                     _snapshotConnection.SelectedItem is not ConnectionNode connection ||
                     _snapshotDatabase.SelectedItem is not DatabaseNode database ||
                     _snapshotObject.SelectedItem is not BackupDocumentRef document )
                {
                    _status.Text = "历史来源必须选择一个完整的结构快照。";
                    return;
                }
                var path = $"{environment.Name} / {version.Name} / {connection.NameAtFirstBackup} / {database.Name} / {document.Name}";
                Source = new SnapshotCompareSource(document.Id, path);
            }
            else
            {
                if ( _liveConnection.SelectedItem is not MySqlConnectionProfile connection ||
                     _liveDatabase.SelectedItem is not DatabaseInfo database ||
                     _liveObject.SelectedItem is not DatabaseObjectInfo item )
                {
                    _status.Text = "实时来源必须选择连接、数据库和对象。";
                    return;
                }
                Source = new LiveCompareSource(connection.Id, database.Name, item,
                    $"实时数据库 / {connection.Name} / {database.Name} / {(item.Kind == SchemaObjectKind.View ? "视图" : "表")} / {item.Name}");
            }
            DialogResult = true;
        }
    }

    private sealed class MigrationSqlWindow : Window
    {
        private readonly SchemaComparisonResult _comparison;
        private readonly TabControl _tabs = new();
        private readonly ComboBox _direction = new() { Height = 34, Width = 120, ItemsSource = new[] { "A → B", "B → A" }, SelectedIndex = 0 };
        private readonly TextBox _sql = CodeBox();

        public MigrationSqlWindow(SchemaComparisonResult comparison)
        {
            _comparison = comparison;
            Title = "DDL / 结构变更建议";
            Width = 920;
            Height = 700;
            MinWidth = 700;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var generatedPanel = new DockPanel();
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            toolbar.Children.Add(new TextBlock { Text = "方向", VerticalAlignment = VerticalAlignment.Center,
                Foreground = AppResources.GetRequired<Brush>("ToolboxInk"), Margin = new Thickness(0, 0, 8, 0) });
            toolbar.Children.Add(_direction);
            var warning = new TextBlock { Text = "仅供审阅，不会执行；可能需要人工补充。", VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Firebrick, Margin = new Thickness(12, 0, 0, 0) };
            toolbar.Children.Add(warning);
            _direction.SelectionChanged += (_, _) => RenderSql();
            DockPanel.SetDock(toolbar, Dock.Top);
            generatedPanel.Children.Add(toolbar);
            generatedPanel.Children.Add(_sql);
            _tabs.Items.Add(new TabItem { Header = "变更 SQL", Content = generatedPanel });
            _tabs.Items.Add(new TabItem { Header = "原始 DDL A", Content = CodeBox(comparison.SourceA.OriginalDdl ?? "A 来源没有原始 DDL。") });
            _tabs.Items.Add(new TabItem { Header = "原始 DDL B", Content = CodeBox(comparison.SourceB.OriginalDdl ?? "B 来源没有原始 DDL。") });
            Grid.SetRow(_tabs, 1);
            root.Children.Add(_tabs);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var copy = new MySqlButton { Content = "复制", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
            copy.Click += (_, _) => { var text = CurrentText(); if ( !string.IsNullOrEmpty(text) ) Clipboard.SetText(text); };
            var save = new MySqlButton { Content = "导出 .sql", Style = AppResources.GetRequired<Style>("ToolboxSoftButton"), Margin = new Thickness(8, 0, 0, 0) };
            save.Click += async (_, _) => await ExportSqlAsync();
            buttons.Children.Add(copy);
            buttons.Children.Add(save);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            Content = root;
            RenderSql();
        }

        private void RenderSql()
        {
            var draft = MigrationSqlGenerator.Generate(_comparison.SourceA, _comparison.SourceB,
                aToB: _direction.SelectedIndex == 0);
            _sql.Text = draft.Sql;
        }

        private string CurrentText() => (_tabs.SelectedItem as TabItem)?.Content switch
        {
            TextBox text => text.Text,
            DockPanel panel when panel.Children.OfType<TextBox>().FirstOrDefault() is { } text => text.Text,
            _ => string.Empty
        };

        private async Task ExportSqlAsync()
        {
            var save = new SaveFileDialog { Filter = "SQL 文件 (*.sql)|*.sql", FileName = "mysql-schema-diff.sql" };
            if ( save.ShowDialog(this) == true )
                await File.WriteAllTextAsync(save.FileName, CurrentText(), new System.Text.UTF8Encoding(false));
        }

        private static TextBox CodeBox(string text = "") => new()
        {
            Text = text,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }
}
