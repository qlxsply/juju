using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Juju.App.Resources;
using Juju.Tools.MySqlSchema.Application;
using Juju.Tools.MySqlSchema.Domain;
using MySqlButton = System.Windows.Controls.Button;
using MySqlTextBox = System.Windows.Controls.TextBox;

namespace Juju.App.Views;

public partial class MySqlSchemaWindow : Window
{
    private readonly MySqlSchemaWorkspace _workspace;
    private readonly ObservableCollection<ConnectionGridRow> _connections = [];
    private readonly Dictionary<string, ConnectionStatusInfo> _connectionStatuses = new(StringComparer.Ordinal);
    private readonly Dictionary<MySqlSchemaPage, Border> _tabIndicators = [];
    private readonly Dictionary<MySqlSchemaPage, MySqlButton> _tabButtons = [];
    private FrameworkElement? _connectionsPage;
    private FrameworkElement? _backupsPage;
    private FrameworkElement? _comparePage;
    private Grid _pageHost = null!;
    private MySqlSchemaPage _currentPage;
    private DataGrid? _connectionsGrid;
    private TextBlock? _connectionsEmpty;
    private TextBlock? _connectionSearchHint;
    private TreeView? _backupTree;
    private BackupSelection? _selectedBackup;

    public MySqlSchemaWindow(MySqlSchemaWorkspace workspace)
    {
        _workspace = workspace;
        InitializeComponent();
        BuildShell();
        Loaded += async (_, _) =>
        {
            await RefreshConnectionsAsync();
            ShowConnectionsPage();
        };
        ToolWindowChrome.SetStatusLeft(this, "MySQL Schema");
    }

    private void BuildShell()
    {
        Root.Background = Brushes.White;
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        tabs.Children.Add(NavigationTab("连接管理", MySqlSchemaPage.Connections, ShowConnectionsPage));
        tabs.Children.Add(NavigationTab("结构备份", MySqlSchemaPage.Backups, async () => await ShowBackupPageAsync()));
        tabs.Children.Add(NavigationTab("结构对比", MySqlSchemaPage.Compare, ShowComparePage));
        var tabBar = new Border
        {
            BorderBrush = AppResources.GetRequired<Brush>("ToolboxLine"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 42,
            Child = tabs
        };
        Root.Children.Add(tabBar);

        _pageHost = new Grid();
        Grid.SetRow(_pageHost, 1);
        Root.Children.Add(_pageHost);
    }

    private Border NavigationTab(string text, MySqlSchemaPage page, Action onClick)
    {
        var layout = new Grid();
        layout.Height = 42;
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
        var button = new MySqlButton
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 0, 14, 0),
            FontSize = 13,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => onClick();
        _tabButtons.Add(page, button);
        layout.Children.Add(button);
        var indicator = new Border { Background = Brushes.Transparent };
        Grid.SetRow(indicator, 1);
        layout.Children.Add(indicator);
        _tabIndicators.Add(page, indicator);
        var container = new Border { Child = layout };
        return container;
    }

    private void SelectPage(MySqlSchemaPage page)
    {
        var view = page switch
        {
            MySqlSchemaPage.Connections => _connectionsPage ??= BuildConnectionsPage(),
            MySqlSchemaPage.Backups => _backupsPage ??= BuildBackupPage(),
            _ => _comparePage ??= BuildComparePage()
        };
        if ( _currentPage == page && _pageHost.Children.Count == 1 && ReferenceEquals(_pageHost.Children[0], view) )
            return;
        _currentPage = page;
        UpdateNavigationTabs();
        _pageHost.Children.Clear();
        _pageHost.Children.Add(view);
    }

    private void ShowConnectionsPage()
    {
        SelectPage(MySqlSchemaPage.Connections);
        UpdateConnectionCountStatus();
        ToolWindowChrome.SetStatusCenter(this, "");
    }

    private async Task ShowBackupPageAsync()
    {
        var firstLoad = _backupsPage is null;
        SelectPage(MySqlSchemaPage.Backups);
        ToolWindowChrome.SetStatusCenter(this, "");
        ToolWindowChrome.SetStatusRight(this, "");
        SetStatus("历史结构文档独立保存在本地，不依赖原连接。");
        if ( firstLoad && _backupTree is not null )
            await RefreshBackupTreeAsync();
    }

    private void ShowComparePage()
    {
        SelectPage(MySqlSchemaPage.Compare);
        SetStatus("对比基于结构属性；不会执行结构变更 SQL。", "结构对比");
    }

    private FrameworkElement PageShell(string title, string description, out StackPanel content)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        content = new StackPanel { Margin = new Thickness(24, 22, 24, 20) };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = AppResources.GetRequired<Brush>("ToolboxInk")
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 5, 0, 18),
            FontSize = 12,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            TextWrapping = TextWrapping.Wrap
        });
        scroll.Content = content;
        return scroll;
    }

    private Border Card(FrameworkElement child) => new()
    {
        Style = AppResources.GetRequired<Style>("ToolboxSettingsCard"),
        Margin = new Thickness(0, 0, 0, 14),
        Child = child
    };

    private MySqlButton ActionButton(string text, Action onClick, bool primary = false)
    {
        var button = new MySqlButton
        {
            Content = text,
            Style = AppResources.GetRequired<Style>(primary ? "ToolboxSoftButton" : "ToolboxHeaderButton"),
            Margin = new Thickness(0, 0, 7, 0),
            Padding = primary ? new Thickness(12, 7, 12, 7) : new Thickness(10, 7, 10, 7)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static MySqlTextBox Input(string value = "", double width = 240) => new()
    {
        Text = value,
        Width = width,
        Height = 34,
        Style = AppResources.GetRequired<Style>("ToolboxTextInput"),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private void SetStatus(string message, string? center = null)
    {
        if ( _currentPage == MySqlSchemaPage.Connections && _connectionsGrid is not null )
        {
            UpdateConnectionCountStatus();
            ToolWindowChrome.SetStatusCenter(this, message);
            return;
        }
        ToolWindowChrome.SetStatusLeft(this, message);
        if ( center is not null )
            ToolWindowChrome.SetStatusCenter(this, center);
    }

    private async Task RefreshConnectionsAsync()
    {
        try
        {
            var profiles = await _workspace.ListConnectionsAsync();
            _connections.Clear();
            foreach ( var profile in profiles )
                _connections.Add(new ConnectionGridRow(profile,
                    _connectionStatuses.GetValueOrDefault(profile.Id, ConnectionStatusInfo.NotTested)));
            if ( _connectionsGrid is not null )
                ApplyConnectionFilter();
        }
        catch ( Exception error )
        { SetStatus("读取连接配置失败：" + error.Message, "连接管理"); }
    }

    private ConnectionGridRow? SelectedConnection => _connectionsGrid?.SelectedItem as ConnectionGridRow;

    private enum MySqlSchemaPage { Connections, Backups, Compare }

    private sealed record ConnectionStatusInfo(string Text, string Kind, string? Details = null)
    {
        public static ConnectionStatusInfo NotTested { get; } = new("未测试", "Neutral");
    }

    private sealed record ConnectionGridRow(MySqlConnectionProfile Profile, ConnectionStatusInfo ConnectionStatus)
    {
        public string Name => Profile.Name;
        public string Host => Profile.Host;
        public uint Port => Profile.Port;
        public string UserName => Profile.UserName;
        public string DefaultDatabase => Profile.DefaultDatabase ?? "—";
        public string Status => ConnectionStatus.Text;
        public string StatusKind => ConnectionStatus.Kind;
        public string? StatusDetails => ConnectionStatus.Details;
        public bool CanOperate => StatusKind != "Testing";
    }

    private void UpdateNavigationTabs()
    {
        foreach ( var (page, indicator) in _tabIndicators )
        {
            var active = page == _currentPage;
            indicator.Background = active
                ? new SolidColorBrush(Color.FromRgb(53, 104, 221))
                : Brushes.Transparent;
            _tabButtons[page].Foreground = AppResources.GetRequired<Brush>(active ? "ToolboxAccent" : "ToolboxMuted");
            _tabButtons[page].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
