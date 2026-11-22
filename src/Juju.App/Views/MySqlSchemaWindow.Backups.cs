using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Juju.App.Resources;
using Juju.Tools.MySqlSchema.Application;
using Juju.Tools.MySqlSchema.Domain;
using MySqlButton = System.Windows.Controls.Button;

namespace Juju.App.Views;

public partial class MySqlSchemaWindow
{
    private TextBlock? _backupSelectionTitle;
    private TextBlock? _backupSelectionInfo;
    private TextBox? _inlineTreeEditor;

    private FrameworkElement BuildBackupPage()
    {
        var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var treeCard = Card(new Grid());
        Grid.SetColumn(treeCard, 0);
        var treeLayout = (Grid) treeCard.Child;
        treeLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        treeLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var treeActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        treeActions.Children.Add(ActionButton("＋ 环境", () => _ = AddEnvironmentAsync(), primary: true));
        treeActions.Children.Add(ActionButton("＋ 版本", () => _ = AddVersionAsync()));
        treeLayout.Children.Add(treeActions);
        _backupTree = new TreeView
        {
            Margin = new Thickness(8, 0, 8, 10),
            BorderThickness = new Thickness(0),
            Background = Brushes.White
        };
        _backupTree.SelectedItemChanged += (_, _) => UpdateBackupSelection();
        Grid.SetRow(_backupTree, 1);
        treeLayout.Children.Add(_backupTree);
        root.Children.Add(workspace);
        workspace.Children.Add(treeCard);

        var detailCard = Card(new StackPanel());
        Grid.SetColumn(detailCard, 2);
        workspace.Children.Add(detailCard);
        var detail = (StackPanel) detailCard.Child;
        var detailActions = new WrapPanel { Margin = new Thickness(12, 12, 12, 6) };
        detailActions.Children.Add(ActionButton("结构备份", () => _ = OpenBackupWizardAsync(), primary: true));
        detailActions.Children.Add(ActionButton("重命名", () => _ = RenameBackupNodeAsync()));
        detailActions.Children.Add(ActionButton("删除", () => _ = DeleteBackupNodeAsync()));
        detailActions.Children.Add(ActionButton("查看快照", () => _ = OpenSelectedSnapshotAsync()));
        detailActions.Children.Add(ActionButton("导出 JSON", () => _ = ExportSelectedSnapshotAsync()));
        detail.Children.Add(detailActions);
        _backupSelectionTitle = new TextBlock
        {
            Text = "选择左侧环境、版本、连接、数据库或备份文档",
            Margin = new Thickness(16, 8, 16, 3),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppResources.GetRequired<Brush>("ToolboxInk"),
            TextWrapping = TextWrapping.Wrap
        };
        detail.Children.Add(_backupSelectionTitle);
        _backupSelectionInfo = new TextBlock
        {
            Text = "环境和版本由你创建；连接来自连接管理，数据库和对象从 MySQL 实时读取。",
            Margin = new Thickness(16, 0, 16, 14),
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            TextWrapping = TextWrapping.Wrap
        };
        detail.Children.Add(_backupSelectionInfo);
        var detailsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        detail.Children.Add(detailsScroll);
        return root;
    }

    private async Task RefreshBackupTreeAsync()
    {
        if ( _backupTree is null ) return;
        try
        {
            var tree = await _workspace.ReadBackupTreeAsync();
            _backupTree.Items.Clear();
            foreach ( var environment in tree.Environments )
            {
                var envItem = NodeItem("◈ " + environment.Name,
                    new BackupSelection(BackupNodeKind.Environment, environment.Id, null, null, null, null));
                foreach ( var version in environment.Versions )
                {
                    var versionItem = NodeItem("▣ " + version.Name,
                        new BackupSelection(BackupNodeKind.Version, environment.Id, version.Id, null, null, null));
                    foreach ( var connection in version.Connections )
                    {
                        var connectionItem = NodeItem("⌁ " + connection.NameAtFirstBackup,
                            new BackupSelection(BackupNodeKind.Connection, environment.Id, version.Id,
                                connection.ProfileId, null, null));
                        foreach ( var database in connection.Databases )
                        {
                            var databaseItem = NodeItem("▤ " + database.Name,
                                new BackupSelection(BackupNodeKind.Database, environment.Id, version.Id,
                                    connection.ProfileId, database.Name, null));
                            foreach ( var document in database.Documents.OrderBy(item => item.Name,
                                         StringComparer.OrdinalIgnoreCase) )
                            {
                                databaseItem.Items.Add(NodeItem((document.Kind == SchemaObjectKind.View ? "◇ " : "▦ ") +
                                    document.Name, new BackupSelection(BackupNodeKind.Document, environment.Id,
                                    version.Id, connection.ProfileId, database.Name, document)));
                            }
                            connectionItem.Items.Add(databaseItem);
                        }
                        versionItem.Items.Add(connectionItem);
                    }
                    envItem.Items.Add(versionItem);
                }
                _backupTree.Items.Add(envItem);
            }
            UpdateBackupSelection();
        }
        catch ( Exception error )
        { SetStatus("读取备份目录失败：" + error.Message); }
    }

    private static TreeViewItem NodeItem(string header, BackupSelection selection) => new()
    {
        Header = header,
        Tag = selection,
        IsExpanded = true,
        Padding = new Thickness(3, 4, 3, 4),
        Foreground = AppResources.GetRequired<Brush>("ToolboxInk")
    };

    private void UpdateBackupSelection()
    {
        _selectedBackup = (_backupTree?.SelectedItem as TreeViewItem)?.Tag as BackupSelection;
        if ( _backupSelectionTitle is null || _backupSelectionInfo is null ) return;
        _backupSelectionTitle.Text = _selectedBackup?.Document?.Name ?? _selectedBackup?.DatabaseName ??
            (_selectedBackup?.Kind == BackupNodeKind.Version ? "版本" :
                _selectedBackup?.Kind == BackupNodeKind.Environment ? "环境" :
                    _selectedBackup?.Kind == BackupNodeKind.Connection ? "连接" : "备份目录");
        _backupSelectionInfo.Text = _selectedBackup switch
        {
            { Kind: BackupNodeKind.Document, Document: { } document } =>
                $"对象类型：{(document.Kind == SchemaObjectKind.View ? "视图" : "表")}\n数据库：{documentPath(_selectedBackup)}\n备份时间：{document.CapturedAtUtc.LocalDateTime:g}",
            { Kind: BackupNodeKind.Database } node => $"数据库：{node.DatabaseName}\n可备份对象会在向导中从当前连接重新发现。",
            { Kind: BackupNodeKind.Connection } => "从此连接节点启动备份时，环境和版本会固定；请选取实时数据库。",
            { Kind: BackupNodeKind.Version } => "",
            { Kind: BackupNodeKind.Environment } => "从此环境节点启动备份时，环境会固定。",
            _ => "选择左侧树节点查看信息，或在根目录下新增环境。"
        };
        _backupSelectionInfo.Visibility = string.IsNullOrWhiteSpace(_backupSelectionInfo.Text)
            ? Visibility.Collapsed : Visibility.Visible;
        SetStatus(_selectedBackup?.Kind == BackupNodeKind.Document ? "历史结构可离线查看。" : "");
    }

    private static string documentPath(BackupSelection selection) =>
        string.Join(" / ", new[] { selection.EnvironmentId, selection.VersionId, selection.ConnectionId,
            selection.DatabaseName }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private async Task AddEnvironmentAsync()
    {
        if ( _backupTree is null || _inlineTreeEditor is not null ) return;
        var pending = new TreeViewItem
        {
            Header = "◈ ",
            Padding = new Thickness(3, 4, 3, 4),
            Foreground = AppResources.GetRequired<Brush>("ToolboxInk")
        };
        _backupTree.Items.Add(pending);
        pending.IsSelected = true;
        BeginInlineTreeEdit(pending, "", async name =>
        {
            var environment = await _workspace.AddEnvironmentAsync(name);
            await RefreshBackupTreeAsync();
            SelectBackupTreeNode(environment.Id, null);
            SetStatus("环境已创建。");
        }, () => _backupTree.Items.Remove(pending), isNew: true);
    }

    private void BeginInlineTreeEdit(TreeViewItem item, string initialValue, Func<string, Task> save,
        Action cancel, bool isNew)
    {
        if ( _inlineTreeEditor is not null ) return;
        var originalHeader = item.Header;
        var editor = new TextBox
        {
            Text = initialValue,
            Width = 220,
            Height = 30,
            Style = AppResources.GetRequired<Style>("ToolboxTextInput"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = true
        };
        _inlineTreeEditor = editor;
        item.Header = editor;
        item.IsSelected = true;
        var finished = false;

        async Task FinishAsync(bool commit)
        {
            if ( finished ) return;
            if ( !commit )
            {
                finished = true;
                item.Header = originalHeader;
                _inlineTreeEditor = null;
                cancel();
                return;
            }

            var value = editor.Text.Trim();
            if ( string.IsNullOrWhiteSpace(value) )
            {
                if ( isNew )
                {
                    finished = true;
                    _inlineTreeEditor = null;
                    cancel();
                    return;
                }
                SetStatus("名称不能为空。");
                editor.Focus();
                return;
            }

            finished = true;
            try
            {
                await save(value);
                _inlineTreeEditor = null;
            }
            catch ( Exception error )
            {
                finished = false;
                SetStatus(error.Message, "操作失败");
                editor.Focus();
                editor.SelectAll();
            }
        }

        editor.KeyDown += async (_, args) =>
        {
            if ( args.Key == Key.Enter )
            {
                args.Handled = true;
                await FinishAsync(commit: true);
            }
            else if ( args.Key == Key.Escape )
            {
                args.Handled = true;
                await FinishAsync(commit: false);
            }
        };
        editor.LostKeyboardFocus += async (_, _) => await FinishAsync(commit: true);
        Dispatcher.BeginInvoke(new Action(() =>
        {
            editor.Focus();
            editor.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void SelectBackupTreeNode(string environmentId, string? versionId)
    {
        if ( _backupTree is null ) return;
        var environment = _backupTree.Items.OfType<TreeViewItem>()
            .FirstOrDefault(item => item.Tag is BackupSelection node && node.EnvironmentId == environmentId);
        if ( environment is null ) return;
        if ( versionId is null )
        {
            environment.IsSelected = true;
            return;
        }
        environment.IsExpanded = true;
        var version = environment.Items.OfType<TreeViewItem>()
            .FirstOrDefault(item => item.Tag is BackupSelection node && node.VersionId == versionId);
        if ( version is not null ) version.IsSelected = true;
    }

    private async Task AddVersionAsync()
    {
        if ( _selectedBackup is not { Kind: BackupNodeKind.Environment } environment )
        {
            SetStatus("请先选中一个环境，再新增版本。");
            return;
        }
        var name = PromptText("新增版本", "版本名称", "");
        if ( name is null ) return;
        await RunUiOperationAsync(async () =>
        {
            await _workspace.AddVersionAsync(environment.EnvironmentId, name);
            await RefreshBackupTreeAsync();
            SetStatus("版本已创建。");
        });
    }

    private async Task RenameBackupNodeAsync()
    {
        if ( _selectedBackup is not { Kind: BackupNodeKind.Environment or BackupNodeKind.Version } selected )
        {
            SetStatus("只能重命名环境或版本。");
            return;
        }
        var current = _selectedBackupTitle();
        if ( _inlineTreeEditor is not null || _backupTree?.SelectedItem is not TreeViewItem item ) return;
        BeginInlineTreeEdit(item, current, async name =>
        {
            if ( selected.Kind == BackupNodeKind.Environment )
                await _workspace.RenameEnvironmentAsync(selected.EnvironmentId, name);
            else
                await _workspace.RenameVersionAsync(selected.EnvironmentId, selected.VersionId!, name);
            await RefreshBackupTreeAsync();
            SelectBackupTreeNode(selected.EnvironmentId,
                selected.Kind == BackupNodeKind.Version ? selected.VersionId : null);
            SetStatus("名称已更新。");
        }, () => { }, isNew: false);
    }

    private string _selectedBackupTitle()
    {
        var node = _backupTree?.SelectedItem as TreeViewItem;
        return node?.Header?.ToString()?.Substring(2) ?? "";
    }

    private async Task DeleteBackupNodeAsync()
    {
        if ( _selectedBackup is not { } selected )
        {
            SetStatus("请先选中要删除的节点。");
            return;
        }
        var description = selected.Kind switch
        {
            BackupNodeKind.Environment => "环境及其下所有版本和备份文档",
            BackupNodeKind.Version => "版本及其下所有连接和备份文档",
            BackupNodeKind.Connection => "此备份树中的连接目录及其结构文档（连接管理中的连接配置不会删除）",
            BackupNodeKind.Database => "此数据库目录及其结构文档",
            BackupNodeKind.Document => $"结构文档“{selected.Document!.Name}”",
            _ => "该目录节点不支持直接删除；请删除具体备份文档。"
        };
        if ( selected.Kind is not (BackupNodeKind.Environment or BackupNodeKind.Version or BackupNodeKind.Connection or
                BackupNodeKind.Database or BackupNodeKind.Document) )
        {
            SetStatus(description);
            return;
        }
        var descendantCount = await CountBackupDocumentsAsync(selected);
        var countText = selected.Kind == BackupNodeKind.Document ? "" : $"（包含 {descendantCount} 份结构文档）";
        var answer = MessageBox.Show(this, $"确定删除{description}{countText}吗？此操作不可撤销。", "删除结构备份",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if ( answer != MessageBoxResult.Yes ) return;
        await RunUiOperationAsync(async () =>
        {
            switch ( selected.Kind )
            {
                case BackupNodeKind.Environment:
                    await _workspace.DeleteEnvironmentAsync(selected.EnvironmentId);
                    break;
                case BackupNodeKind.Version:
                    await _workspace.DeleteVersionAsync(selected.EnvironmentId, selected.VersionId!);
                    break;
                case BackupNodeKind.Connection:
                    await _workspace.DeleteBackupConnectionAsync(selected.EnvironmentId, selected.VersionId!,
                        selected.ConnectionId!);
                    break;
                case BackupNodeKind.Database:
                    await _workspace.DeleteBackupDatabaseAsync(selected.EnvironmentId, selected.VersionId!,
                        selected.ConnectionId!, selected.DatabaseName!);
                    break;
                case BackupNodeKind.Document:
                    await _workspace.DeleteSnapshotAsync(selected.EnvironmentId, selected.VersionId!,
                        selected.ConnectionId!, selected.DatabaseName!, selected.Document!.Id);
                    break;
            }
            await RefreshBackupTreeAsync();
            SetStatus("备份目录或文档已删除。");
        });
    }

    private async Task<int> CountBackupDocumentsAsync(BackupSelection selection)
    {
        if ( selection.Kind == BackupNodeKind.Document ) return 1;
        var tree = await _workspace.ReadBackupTreeAsync();
        var environment = tree.Environments.SingleOrDefault(item => item.Id == selection.EnvironmentId);
        if ( environment is null ) return 0;
        var versions = selection.Kind == BackupNodeKind.Environment
            ? environment.Versions
            : environment.Versions.Where(item => item.Id == selection.VersionId).ToArray();
        var count = 0;
        foreach ( var version in versions )
        {
            var connections = selection.Kind is BackupNodeKind.Environment or BackupNodeKind.Version
                ? version.Connections
                : version.Connections.Where(item => item.ProfileId == selection.ConnectionId).ToArray();
            foreach ( var connection in connections )
            {
                var databases = selection.Kind is BackupNodeKind.Environment or BackupNodeKind.Version or BackupNodeKind.Connection
                    ? connection.Databases
                    : connection.Databases.Where(item => string.Equals(item.Name, selection.DatabaseName,
                        StringComparison.OrdinalIgnoreCase)).ToArray();
                count += databases.Sum(database => database.Documents.Count);
            }
        }
        return count;
    }

    private async Task OpenBackupWizardAsync()
    {
        if ( _selectedBackup is not { Kind: BackupNodeKind.Environment or BackupNodeKind.Version or
                BackupNodeKind.Connection or BackupNodeKind.Database } selected )
        {
            SetStatus("请在环境、版本、连接或数据库节点启动结构备份。");
            return;
        }
        var dialog = new BackupWizardWindow(_workspace, selected) { Owner = this };
        if ( dialog.ShowDialog() == true )
        {
            await RefreshBackupTreeAsync();
            SetStatus(dialog.ResultSummary);
        }
    }

    private async Task OpenSelectedSnapshotAsync()
    {
        if ( _selectedBackup is not { Kind: BackupNodeKind.Document, Document: { } document } selected )
            return;
        await RunUiOperationAsync(async () =>
        {
            var snapshot = await _workspace.ReadSnapshotAsync(document.Id);
            new SchemaSnapshotWindow(snapshot) { Owner = this }.ShowDialog();
        });
    }

    private async Task ExportSelectedSnapshotAsync()
    {
        if ( _selectedBackup is not { Kind: BackupNodeKind.Document, Document: { } document } )
            return;
        await RunUiOperationAsync(async () =>
        {
            var snapshot = await _workspace.ReadSnapshotAsync(document.Id);
            var save = new SaveFileDialog
            {
                Title = "导出结构快照",
                Filter = "JSON 文件 (*.json)|*.json",
                FileName = SafeFileName(snapshot.Object.DatabaseName + "_" + snapshot.Object.Name) + ".json"
            };
            if ( save.ShowDialog(this) != true ) return;
            await File.WriteAllTextAsync(save.FileName, JsonSerializer.Serialize(snapshot,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }),
                new System.Text.UTF8Encoding(false));
        });
    }

    private async Task RunUiOperationAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch ( Exception error ) { SetStatus(error.Message, "操作失败"); }
    }

    private static string? PromptText(string title, string label, string current)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 430,
            Height = 185,
            MinWidth = 430,
            MaxWidth = 430,
            MinHeight = 185,
            MaxHeight = 185,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 7) });
        var input = new TextBox { Text = current, Height = 34, Style = AppResources.GetRequired<Style>("ToolboxTextInput") };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new MySqlButton { Content = "取消", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
        var accept = new MySqlButton { Content = "确定", Style = AppResources.GetRequired<Style>("ToolboxSoftButton"), Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        accept.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }

    private enum BackupNodeKind { Environment, Version, Connection, Database, Document }

    private sealed record BackupSelection(BackupNodeKind Kind, string EnvironmentId, string? VersionId,
        string? ConnectionId, string? DatabaseName, BackupDocumentRef? Document);

    private sealed class BackupWizardWindow : Window
    {
        private readonly MySqlSchemaWorkspace _workspace;
        private readonly BackupSelection _context;
        private readonly ComboBox _environment = SelectBox();
        private readonly ComboBox _version = SelectBox();
        private readonly ComboBox _connection = SelectBox();
        private readonly ComboBox _database = SelectBox();
        private readonly ComboBox _kind = SelectBox();
        private readonly TextBox _search = new() { Height = 32, Style = AppResources.GetRequired<Style>("ToolboxTextInput") };
        private readonly StackPanel _candidateRows = new();
        private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        private readonly MySqlButton _backupButton = new() { Content = "开始备份", Style = AppResources.GetRequired<Style>("ToolboxSoftButton") };
        private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<DatabaseObjectInfo> _objects = [];
        private IReadOnlySet<string> _alreadyBacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _updating;

        public string ResultSummary { get; private set; } = "";

        public BackupWizardWindow(MySqlSchemaWorkspace workspace, BackupSelection context)
        {
            _workspace = workspace;
            _context = context;
            Title = "结构备份";
            Width = 830;
            Height = 720;
            MinWidth = 760;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.White;

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock
            {
                Text = "选择目标目录和实时数据库对象",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = AppResources.GetRequired<Brush>("ToolboxInk"),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var paths = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(paths, 1);
            paths.ColumnDefinitions.Add(new ColumnDefinition());
            paths.ColumnDefinitions.Add(new ColumnDefinition());
            paths.ColumnDefinitions.Add(new ColumnDefinition());
            paths.ColumnDefinitions.Add(new ColumnDefinition());
            AddComboField(paths, 0, "环境", _environment);
            AddComboField(paths, 1, "版本", _version);
            AddComboField(paths, 2, "连接", _connection);
            AddComboField(paths, 3, "数据库", _database);
            root.Children.Add(paths);

            var candidatePanel = new Grid();
            candidatePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            candidatePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var candidateHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            candidateHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            candidateHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            candidateHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            candidateHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            candidateHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
            var label = new TextBlock
            {
                Text = "候选对象",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Foreground = AppResources.GetRequired<Brush>("ToolboxInk")
            };
            candidateHeader.Children.Add(label);
            _kind.ItemsSource = new[] { "全部类型", "表", "视图" };
            _kind.SelectedIndex = 0;
            _kind.SelectionChanged += (_, _) => RenderCandidates();
            Grid.SetColumn(_kind, 1);
            candidateHeader.Children.Add(_kind);
            _search.Margin = new Thickness(8, 0, 0, 0);
            _search.ToolTip = "搜索对象名称";
            _search.TextChanged += (_, _) => RenderCandidates();
            Grid.SetColumn(_search, 2);
            candidateHeader.Children.Add(_search);
            var selectAll = new MySqlButton { Content = "全选可备份", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
            selectAll.Click += (_, _) =>
            {
                foreach ( var item in CurrentVisibleObjects() )
                    if ( !_alreadyBacked.Contains(item.Name) ) _selected.Add(item.Name);
                RenderCandidates();
            };
            Grid.SetColumn(selectAll, 3);
            candidateHeader.Children.Add(selectAll);
            var clear = new MySqlButton { Content = "清空", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
            clear.Click += (_, _) => { _selected.Clear(); RenderCandidates(); };
            Grid.SetColumn(clear, 4);
            candidateHeader.Children.Add(clear);
            candidatePanel.Children.Add(candidateHeader);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.Content = _candidateRows;
            Grid.SetRow(scroll, 1);
            candidatePanel.Children.Add(scroll);
            Grid.SetRow(candidatePanel, 2);
            root.Children.Add(candidatePanel);

            _status.Margin = new Thickness(0, 10, 0, 4);
            Grid.SetRow(_status, 3);
            root.Children.Add(_status);
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var cancel = new MySqlButton { Content = "取消", Style = AppResources.GetRequired<Style>("ToolboxHeaderButton") };
            cancel.Click += (_, _) => DialogResult = false;
            _backupButton.IsEnabled = false;
            _backupButton.Click += async (_, _) => await CommitBackupAsync();
            footer.Children.Add(cancel);
            footer.Children.Add(_backupButton);
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);
            Content = root;

            _environment.SelectionChanged += async (_, _) => await EnvironmentChangedAsync();
            _version.SelectionChanged += async (_, _) => await VersionChangedAsync();
            _connection.SelectionChanged += async (_, _) => await ConnectionChangedAsync();
            _database.SelectionChanged += async (_, _) => await DatabaseChangedAsync();
            Loaded += async (_, _) => await InitializeAsync();
        }

        private static ComboBox SelectBox() => new()
        {
            Height = 34,
            Margin = new Thickness(0, 4, 7, 0),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        private static void AddComboField(Grid grid, int column, string title, ComboBox combo)
        {
            var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 7, 0, 0, 0) };
            panel.Children.Add(new TextBlock { Text = title, Foreground = AppResources.GetRequired<Brush>("ToolboxMuted") });
            panel.Children.Add(combo);
            Grid.SetColumn(panel, column);
            grid.Children.Add(panel);
        }

        private async Task InitializeAsync()
        {
            _updating = true;
            try
            {
                var tree = await _workspace.ReadBackupTreeAsync();
                _environment.ItemsSource = tree.Environments;
                _environment.DisplayMemberPath = nameof(EnvironmentNode.Name);
                _environment.SelectedItem = tree.Environments.SingleOrDefault(item => item.Id == _context.EnvironmentId)
                    ?? tree.Environments.FirstOrDefault();
                _environment.IsEnabled = _context.EnvironmentId is null;
                _updating = false;
                await EnvironmentChangedAsync();
            }
            catch ( Exception error ) { _status.Text = error.Message; }
            finally { _updating = false; }
        }

        private async Task EnvironmentChangedAsync()
        {
            if ( _updating ) return;
            var environment = _environment.SelectedItem as EnvironmentNode;
            _updating = true;
            try
            {
                var versions = environment?.Versions ?? [];
                _version.ItemsSource = versions;
                _version.DisplayMemberPath = nameof(VersionNode.Name);
                _version.SelectedItem = versions.SingleOrDefault(item => item.Id == _context.VersionId)
                    ?? versions.FirstOrDefault();
                _version.IsEnabled = _context.VersionId is null;
            }
            finally { _updating = false; }
            await VersionChangedAsync();
        }

        private async Task VersionChangedAsync()
        {
            if ( _updating ) return;
            _updating = true;
            try
            {
                var profiles = await _workspace.ListConnectionsAsync();
                _connection.ItemsSource = profiles;
                _connection.DisplayMemberPath = nameof(MySqlConnectionProfile.Name);
                _connection.SelectedItem = profiles.SingleOrDefault(item => item.Id == _context.ConnectionId);
                if ( _connection.SelectedItem is null && _context.ConnectionId is null )
                    _connection.SelectedItem = profiles.FirstOrDefault();
                _connection.IsEnabled = _context.ConnectionId is null;
            }
            catch ( Exception error ) { _status.Text = error.Message; }
            finally { _updating = false; }
            await ConnectionChangedAsync();
        }

        private async Task ConnectionChangedAsync()
        {
            if ( _updating ) return;
            var profile = _connection.SelectedItem as MySqlConnectionProfile;
            _selected.Clear();
            _objects = [];
            _alreadyBacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _updating = true;
            try
            {
                var databases = profile is null ? [] : await _workspace.ListDatabasesAsync(profile.Id);
                _database.ItemsSource = databases;
                _database.DisplayMemberPath = nameof(DatabaseInfo.Name);
                _database.SelectedItem = databases.SingleOrDefault(item =>
                    string.Equals(item.Name, _context.DatabaseName, StringComparison.OrdinalIgnoreCase))
                    ?? databases.SingleOrDefault(item => string.Equals(item.Name, profile?.DefaultDatabase,
                        StringComparison.OrdinalIgnoreCase)) ?? databases.FirstOrDefault();
                _database.IsEnabled = _context.DatabaseName is null;
                _status.Text = databases.Count == 0 ? "没有可见数据库。" : "连接和数据库已读取，请选择对象。";
            }
            catch ( Exception error )
            {
                _database.ItemsSource = null;
                _status.Text = "无法读取数据库列表：" + error.Message;
            }
            finally { _updating = false; }
            await DatabaseChangedAsync();
        }

        private async Task DatabaseChangedAsync()
        {
            if ( _updating ) return;
            var profile = _connection.SelectedItem as MySqlConnectionProfile;
            var database = _database.SelectedItem as DatabaseInfo;
            _selected.Clear();
            _objects = [];
            _alreadyBacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if ( profile is null || database is null )
            {
                RenderCandidates();
                _status.Text = "请先完成环境、版本、连接和数据库选择。";
                return;
            }
            try
            {
                _status.Text = "正在读取真实数据库对象…";
                _objects = await _workspace.ListObjectsAsync(profile.Id, database.Name);
                if ( _context.EnvironmentId is not null && _context.VersionId is not null )
                    _alreadyBacked = await _workspace.GetBackedObjectNamesAsync(_context.EnvironmentId,
                        _context.VersionId, profile.Id, database.Name);
                RenderCandidates();
                var available = _objects.Count(item => !_alreadyBacked.Contains(item.Name));
                _status.Text = _objects.Count == 0
                    ? "数据库中没有可见表或视图。"
                    : available == 0 ? "此目标路径下的对象已全部备份；删除现有文档后才能重新备份。"
                    : $"发现 {_objects.Count} 个表/视图；其中 {available} 个尚未备份。";
            }
            catch ( Exception error )
            {
                _objects = [];
                RenderCandidates();
                _status.Text = "读取对象失败（不代表数据库为空）：" + error.Message;
            }
        }

        private void RenderCandidates()
        {
            _candidateRows.Children.Clear();
            var visible = CurrentVisibleObjects().ToArray();
            if ( visible.Length == 0 )
            {
                _candidateRows.Children.Add(new TextBlock
                {
                    Text = "没有符合条件的候选对象。",
                    Margin = new Thickness(8, 15, 8, 15),
                    Foreground = AppResources.GetRequired<Brush>("ToolboxMuted")
                });
            }
            foreach ( var item in visible )
            {
                var backed = _alreadyBacked.Contains(item.Name);
                var check = new CheckBox
                {
                    Content = $"{item.Name}    {(item.Kind == SchemaObjectKind.View ? "视图" : "表")}{(backed ? "    已备份（禁止覆盖）" : "")}",
                    IsChecked = _selected.Contains(item.Name),
                    IsEnabled = !backed,
                    Margin = new Thickness(8, 7, 8, 7),
                    Foreground = backed ? AppResources.GetRequired<Brush>("ToolboxMuted") : AppResources.GetRequired<Brush>("ToolboxInk")
                };
                check.Checked += (_, _) => { _selected.Add(item.Name); UpdateBackupButton(); };
                check.Unchecked += (_, _) => { _selected.Remove(item.Name); UpdateBackupButton(); };
                _candidateRows.Children.Add(check);
            }
            _status.Text = $"候选对象 {visible.Length} · 已选 {_selected.Count} · 已备份 {_alreadyBacked.Count}";
            UpdateBackupButton();
        }

        private IEnumerable<DatabaseObjectInfo> CurrentVisibleObjects()
        {
            var search = _search.Text.Trim();
            var kind = _kind.SelectedIndex;
            return _objects.Where(item =>
                (kind == 0 || kind == 1 && item.Kind == SchemaObjectKind.Table || kind == 2 && item.Kind == SchemaObjectKind.View) &&
                (string.IsNullOrEmpty(search) || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        private void UpdateBackupButton() => _backupButton.IsEnabled = _selected.Count > 0 &&
            _environment.SelectedItem is not null && _version.SelectedItem is not null &&
            _connection.SelectedItem is not null && _database.SelectedItem is not null;

        private async Task CommitBackupAsync()
        {
            if ( _environment.SelectedItem is not EnvironmentNode environment ||
                 _version.SelectedItem is not VersionNode version ||
                 _connection.SelectedItem is not MySqlConnectionProfile profile ||
                 _database.SelectedItem is not DatabaseInfo database )
                return;
            var selected = _objects.Where(item => _selected.Contains(item.Name) && !_alreadyBacked.Contains(item.Name)).ToArray();
            if ( selected.Length == 0 ) return;
            _backupButton.IsEnabled = false;
            _status.Text = $"正在备份 {selected.Length} 个对象…";
            try
            {
                var results = await _workspace.BackupAsync(environment.Id, version.Id, profile.Id, database.Name, selected);
                var succeeded = results.Count(result => result.Success);
                ResultSummary = $"批量备份完成：成功 {succeeded}，失败 {results.Count - succeeded}。";
                _status.Text = ResultSummary + Environment.NewLine + string.Join(Environment.NewLine,
                    results.Where(result => !result.Success).Select(result => $"{result.ObjectName}：{result.Message}"));
                foreach ( var result in results.Where(result => result.Success) )
                    _alreadyBacked = new HashSet<string>(_alreadyBacked.Append(result.ObjectName), StringComparer.OrdinalIgnoreCase);
                _selected.Clear();
                RenderCandidates();
                if ( succeeded > 0 && succeeded == results.Count )
                    DialogResult = true;
            }
            catch ( Exception error )
            { _status.Text = "备份失败：" + error.Message; }
            finally { UpdateBackupButton(); }
        }
    }

    private sealed class SchemaSnapshotWindow : Window
    {
        private static readonly JsonSerializerOptions Options =
            new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public SchemaSnapshotWindow(SchemaSnapshot snapshot)
        {
            Title = $"结构快照 · {snapshot.Object.Name}";
            Width = 1050;
            Height = 760;
            MinWidth = 800;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var description = new TextBlock
            {
                Text = $"{snapshot.EnvironmentName} / {snapshot.VersionName} / {snapshot.ConnectionNameAtCapture} / " +
                       $"{snapshot.Object.DatabaseName} / {snapshot.Object.Name}    " +
                       $"{(snapshot.Object.Kind == SchemaObjectKind.View ? "视图" : "表")}    " +
                       $"{snapshot.CapturedAtUtc.LocalDateTime:g}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 12),
                Foreground = AppResources.GetRequired<Brush>("ToolboxMuted")
            };
            root.Children.Add(description);

            var tabs = new TabControl();
            AddTab(tabs, "数据库", snapshot.Object.Database);
            AddTab(tabs, "字段", snapshot.Object.Columns);
            AddTab(tabs, "主键/索引", snapshot.Object.Indexes);
            AddTab(tabs, "外键", snapshot.Object.ForeignKeys);
            AddTab(tabs, "检查约束", snapshot.Object.CheckConstraints);
            AddTab(tabs, "分区", snapshot.Object.Partitions);
            AddTab(tabs, "表选项", snapshot.Object.Table);
            AddTab(tabs, "视图属性", snapshot.Object.View);
            AddTab(tabs, "采集告警", snapshot.Object.CaptureWarnings);
            var ddl = new TextBox
            {
                Text = snapshot.Object.OriginalDdl ?? "未从 MySQL 读取到原始 DDL。",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            tabs.Items.Add(new TabItem { Header = "原始 DDL", Content = ddl });
            Grid.SetRow(tabs, 1);
            root.Children.Add(tabs);
            Content = root;
        }

        private static void AddTab(TabControl tabs, string title, object? value)
        {
            var view = new TextBox
            {
                Text = JsonSerializer.Serialize(value, Options),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            tabs.Items.Add(new TabItem { Header = title, Content = view });
        }
    }
}
