using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Juju.App.Resources;
using Juju.Tools.MySqlSchema.Domain;
using MySqlButton = System.Windows.Controls.Button;
using MySqlTextBox = System.Windows.Controls.TextBox;

namespace Juju.App.Views;

public partial class MySqlSchemaWindow
{
    private MySqlTextBox? _connectionSearch;
    private ICollectionView? _connectionView;

    private FrameworkElement BuildConnectionsPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid { Margin = new Thickness(16, 12, 16, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        var add = ConnectionButton("新增", () => _ = OpenConnectionEditorAsync(null), ConnectionButtonTone.Primary);
        add.Margin = new Thickness(0);
        toolbar.Children.Add(add);

        var searchBox = new Grid { VerticalAlignment = VerticalAlignment.Center };
        _connectionSearch = Input("", double.NaN);
        _connectionSearch.Height = 34;
        _connectionSearch.Margin = new Thickness(0);
        _connectionSearch.ToolTip = "搜索连接名称、主机";
        _connectionSearch.TextChanged += (_, _) =>
        {
            if ( _connectionSearchHint is not null )
                _connectionSearchHint.Visibility = string.IsNullOrEmpty(_connectionSearch.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            ApplyConnectionFilter();
        };
        searchBox.Children.Add(_connectionSearch);
        _connectionSearchHint = new TextBlock
        {
            Text = "⌕ 搜索连接名称、主机",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            IsHitTestVisible = false
        };
        searchBox.Children.Add(_connectionSearchHint);
        Grid.SetColumn(searchBox, 2);
        toolbar.Children.Add(searchBox);
        root.Children.Add(toolbar);

        var tableArea = new Grid();
        tableArea.Margin = new Thickness(0, 0, 0, 0);
        Grid.SetRow(tableArea, 1);
        _connectionsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            FocusVisualStyle = null,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(237, 240, 245)),
            VerticalGridLinesBrush = Brushes.Transparent,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            RowHeight = 42,
            ColumnHeaderHeight = 38,
            RowStyle = ConnectionRowStyle(),
            CellStyle = ConnectionCellStyle(),
            ColumnHeaderStyle = ConnectionHeaderStyle()
        };
        _connectionsGrid.Columns.Add(TextColumn("连接名称", nameof(ConnectionGridRow.Name), 1.2));
        _connectionsGrid.Columns.Add(TextColumn("主机", nameof(ConnectionGridRow.Host), 1.3));
        _connectionsGrid.Columns.Add(TextColumn("端口", nameof(ConnectionGridRow.Port), 0.55));
        _connectionsGrid.Columns.Add(TextColumn("用户名", nameof(ConnectionGridRow.UserName), 1));
        _connectionsGrid.Columns.Add(TextColumn("默认数据库", nameof(ConnectionGridRow.DefaultDatabase), 1.1));
        _connectionsGrid.Columns.Add(StatusColumn());
        _connectionsGrid.Columns.Add(ActionColumn());
        _connectionsGrid.MouseDoubleClick += (_, args) =>
        {
            if ( !IsInsideButton(args.OriginalSource as DependencyObject) &&
                 args.OriginalSource is FrameworkElement element && element.DataContext is ConnectionGridRow row )
                _ = ShowConnectionDetailsAsync(row);
        };
        _connectionsGrid.PreviewKeyDown += HandleConnectionGridPreviewKeyDown;
        tableArea.Children.Add(_connectionsGrid);
        _connectionsEmpty = new TextBlock
        {
            Text = "暂无连接",
            FontSize = 13,
            Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        tableArea.Children.Add(_connectionsEmpty);
        root.Children.Add(tableArea);

        _connectionView = CollectionViewSource.GetDefaultView(_connections);
        _connectionsGrid.ItemsSource = _connectionView;
        _connections.CollectionChanged += (_, _) => ApplyConnectionFilter();
        ApplyConnectionFilter();
        return root;
    }

    private void ApplyConnectionFilter()
    {
        if ( _connectionsGrid is null || _connectionView is null )
            return;
        var filter = _connectionSearch?.Text.Trim();
        _connectionView.Filter = item => item is ConnectionGridRow row &&
            (string.IsNullOrWhiteSpace(filter) ||
             row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
             row.Host.Contains(filter, StringComparison.OrdinalIgnoreCase));
        _connectionView.Refresh();
        UpdateConnectionCountStatus();
    }

    private void UpdateConnectionCountStatus()
    {
        if ( _connectionsGrid is null )
            return;
        var shown = _connectionView?.Cast<object>().Count() ?? 0;
        ToolWindowChrome.SetStatusLeft(this, $"共 {_connections.Count} 个连接 · 当前显示 {shown} 个");
        if ( _connectionsEmpty is not null )
            _connectionsEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HandleConnectionGridPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if ( args.Key is Key.Left or Key.Right )
        {
            args.Handled = true;
            return;
        }
        if ( args.Key is not (Key.Up or Key.Down) || _connectionsGrid is null || _connectionView is null )
            return;

        var rows = _connectionView.Cast<object>().OfType<ConnectionGridRow>().ToArray();
        if ( rows.Length == 0 )
            return;
        var current = Array.FindIndex(rows, row => row.Profile.Id == SelectedConnection?.Profile.Id);
        var next = current < 0
            ? (args.Key == Key.Up ? rows.Length - 1 : 0)
            : (current + (args.Key == Key.Up ? rows.Length - 1 : 1)) % rows.Length;
        _connectionsGrid.SelectedItem = rows[next];
        _connectionsGrid.ScrollIntoView(rows[next]);
        args.Handled = true;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double starWidth) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(starWidth, DataGridLengthUnitType.Star),
        MinWidth = 65,
        ElementStyle = new Style(typeof(TextBlock))
        {
            Setters =
            {
                new Setter(VerticalAlignmentProperty, VerticalAlignment.Center),
                new Setter(HorizontalAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(MarginProperty, new Thickness(12, 0, 8, 0)),
                new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left),
                new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis)
            }
        }
    };

    private static DataGridColumn StatusColumn()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(ConnectionGridRow.Status)));
        text.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ConnectionGridRow.StatusDetails)));
        text.SetValue(TextBlock.FontSizeProperty, 12d);
        text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            AppResources.GetRequired<Brush>("ToolboxMuted")));
        textStyle.Triggers.Add(StatusTextTrigger("Success", Color.FromRgb(33, 132, 75)));
        textStyle.Triggers.Add(StatusTextTrigger("Failure", Color.FromRgb(196, 76, 64)));
        textStyle.Triggers.Add(StatusTextTrigger("Testing", Color.FromRgb(57, 105, 213)));
        text.SetValue(FrameworkElement.StyleProperty, textStyle);
        return new DataGridTemplateColumn
        {
            Header = "状态",
            Width = new DataGridLength(0.85, DataGridLengthUnitType.Star),
            MinWidth = 88,
            CellTemplate = new DataTemplate { VisualTree = text }
        };
    }

    private static DataTrigger StatusTextTrigger(string kind, Color color)
    {
        var trigger = new DataTrigger { Binding = new Binding(nameof(ConnectionGridRow.StatusKind)), Value = kind };
        trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(color)));
        return trigger;
    }

    private DataGridColumn ActionColumn()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        AddRowButton(panel, "编辑", RowEditClick);
        AddRowButton(panel, "测试", RowTestClick);
        AddRowButton(panel, "删除", RowDeleteClick, danger: true);
        AddRowButton(panel, "复制", RowCopyClick);
        return new DataGridTemplateColumn
        {
            Header = "操作",
            Width = new DataGridLength(280),
            MinWidth = 280,
            CellTemplate = new DataTemplate { VisualTree = panel }
        };
    }

    private static void AddRowButton(FrameworkElementFactory panel, string text,
        RoutedEventHandler handler, bool danger = false)
    {
        var button = new FrameworkElementFactory(typeof(MySqlButton));
        button.SetValue(ContentControl.ContentProperty, text);
        button.SetValue(FrameworkElement.StyleProperty,
            ConnectionButtonStyle(danger ? ConnectionButtonTone.Danger : ConnectionButtonTone.Neutral));
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0));
        button.SetValue(FrameworkElement.WidthProperty, 64d);
        button.SetValue(FrameworkElement.HeightProperty, 32d);
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(ConnectionGridRow.CanOperate)));
        if ( danger )
            button.SetValue(Control.ForegroundProperty, Brushes.Firebrick);
        button.AddHandler(MySqlButton.ClickEvent, handler);
        panel.AppendChild(button);
    }

    private MySqlButton ConnectionButton(string text, Action onClick, ConnectionButtonTone tone)
    {
        var button = new MySqlButton
        {
            Content = text,
            Width = 64,
            Height = 32,
            MinWidth = 64,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = ConnectionButtonStyle(tone)
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Style ConnectionButtonStyle(ConnectionButtonTone tone)
    {
        var (background, border, foreground, hover) = tone switch
        {
            ConnectionButtonTone.Primary => (
                Color.FromRgb(53, 104, 221), Color.FromRgb(53, 104, 221), Colors.White, Color.FromRgb(40, 88, 200)),
            ConnectionButtonTone.Danger => (
                Color.FromRgb(255, 240, 238), Color.FromRgb(243, 211, 206), Color.FromRgb(196, 56, 56), Color.FromRgb(255, 228, 224)),
            _ => (
                Colors.White, Color.FromRgb(220, 227, 237), Color.FromRgb(52, 68, 93), Color.FromRgb(241, 245, 252))
        };
        var style = new Style(typeof(MySqlButton));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(background)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(border)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(foreground)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 4, 0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));

        var surface = new FrameworkElementFactory(typeof(Border)) { Name = "Surface" };
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(ContentPresenter.ContentTemplateProperty,
            new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        presenter.SetValue(ContentPresenter.ContentStringFormatProperty,
            new TemplateBindingExtension(ContentControl.ContentStringFormatProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(presenter);

        var template = new ControlTemplate(typeof(MySqlButton)) { VisualTree = surface };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters = { new Setter(Border.BackgroundProperty, new SolidColorBrush(hover), "Surface") }
        });
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters = { new Setter(UIElement.OpacityProperty, 0.55) }
        });
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private enum ConnectionButtonTone { Neutral, Primary, Danger }

    private static Style ConnectionHeaderStyle() => new(typeof(DataGridColumnHeader))
    {
        Setters =
        {
            new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
            new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            new Setter(Control.PaddingProperty, new Thickness(12, 0, 8, 0)),
            new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(247, 249, 252))),
            new Setter(Control.ForegroundProperty, AppResources.GetRequired<Brush>("ToolboxMuted")),
            new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(237, 240, 245))),
            new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
            new Setter(Control.FontWeightProperty, FontWeights.SemiBold)
        }
    };

    private static Style ConnectionRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(36, 51, 76))));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(237, 240, 245))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        style.Triggers.Add(new Trigger
        {
            Property = DataGridRow.IsMouseOverProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(242, 247, 255))) }
        });
        style.Triggers.Add(new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(234, 240, 255))) }
        });
        return style;
    }

    private static Style ConnectionCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(36, 51, 76))));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Triggers.Add(new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(234, 240, 255))) }
        });
        return style;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while ( source is not null )
        {
            if ( source is MySqlButton )
                return true;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void RowEditClick(object sender, RoutedEventArgs args)
    {
        if ( (sender as FrameworkElement)?.DataContext is ConnectionGridRow row )
            _ = OpenConnectionEditorAsync(row.Profile);
    }

    private void RowTestClick(object sender, RoutedEventArgs args)
    {
        if ( (sender as FrameworkElement)?.DataContext is ConnectionGridRow row )
            _ = TestConnectionAsync(row.Profile);
    }

    private void RowDeleteClick(object sender, RoutedEventArgs args)
    {
        if ( (sender as FrameworkElement)?.DataContext is ConnectionGridRow row )
            _ = DeleteConnectionAsync(row);
    }

    private void RowCopyClick(object sender, RoutedEventArgs args)
    {
        if ( (sender as FrameworkElement)?.DataContext is ConnectionGridRow row )
            _ = CopyConnectionAsync(row);
    }

    private async Task EditConnectionAsync(MySqlConnectionProfile? existing)
    {
        var existingPassword = existing is null
            ? string.Empty
            : await _workspace.GetConnectionPasswordAsync(existing.Id);
        var dialog = new Window
        {
            Title = existing is null ? "新增连接" : "编辑连接",
            Icon = AppResources.GetRequired<ImageSource>("MySqlSchemaToolIcon"),
            Width = 510,
            Height = 610,
            MinWidth = 460,
            MinHeight = 550,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = Brushes.White
        };
        var layout = new Grid { Margin = new Thickness(22, 16, 22, 16) };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var fields = new StackPanel();
        var name = Input(existing?.Name ?? "", double.NaN);
        var host = Input(existing?.Host ?? "", double.NaN);
        var port = Input((existing?.Port ?? 3306).ToString(), double.NaN);
        var user = Input(existing?.UserName ?? "", double.NaN);
        var password = new PasswordBox
        {
            Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(252, 253, 255)),
            BorderBrush = AppResources.GetRequired<Brush>("ToolboxLine"),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.FromRgb(39, 50, 72)),
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5),
            Password = existingPassword,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var visiblePassword = Input(existingPassword, double.NaN);
        visiblePassword.Visibility = Visibility.Collapsed;
        var passwordToggle = ConnectionButton("显示", () => { }, ConnectionButtonTone.Neutral);
        passwordToggle.Margin = new Thickness(6, 0, 0, 0);
        passwordToggle.Click += (_, _) =>
        {
            var show = password.Visibility == Visibility.Visible;
            if ( show ) visiblePassword.Text = password.Password;
            else password.Password = visiblePassword.Text;
            password.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            visiblePassword.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            passwordToggle.Content = show ? "隐藏" : "显示";
        };
        visiblePassword.TextChanged += (_, _) => password.Password = visiblePassword.Text;
        var passwordLine = new Grid();
        passwordLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        passwordLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        passwordLine.Children.Add(password);
        passwordLine.Children.Add(visiblePassword);
        Grid.SetColumn(passwordToggle, 1);
        passwordLine.Children.Add(passwordToggle);

        var defaultDatabase = Input(existing?.DefaultDatabase ?? "", double.NaN);
        var sslMode = new ComboBox
        {
            Height = 34,
            MinHeight = 34,
            ItemsSource = MySqlSslModes.All,
            SelectedItem = existing?.SslMode ?? "Preferred"
        };
        sslMode.Loaded += (_, _) =>
        {
            sslMode.ApplyTemplate();
            if ( sslMode.Template.FindName("Frame", sslMode) is Border frame )
                frame.CornerRadius = new CornerRadius(2);
        };
        AddEditorField(fields, "连接名称 *", name);
        AddEditorField(fields, "主机 *", host);
        AddEditorField(fields, "端口 *", port);
        AddEditorField(fields, "用户名 *", user);
        AddEditorField(fields, "密码", passwordLine);
        AddEditorField(fields, "默认数据库", defaultDatabase);
        AddEditorField(fields, "TLS / SSL", sslMode);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        fields.Children.Add(error);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = fields };
        layout.Children.Add(scroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ConnectionButton("取消", dialog.Close, ConnectionButtonTone.Neutral);
        var save = ConnectionButton("保存", () => { }, ConnectionButtonTone.Primary);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        save.Click += async (_, _) =>
        {
            if ( !uint.TryParse(port.Text, out var portValue) )
            {
                error.Text = "端口必须是 1 到 65535 之间的整数。";
                return;
            }
            try
            {
                var saved = await _workspace.SaveConnectionAsync(new ConnectionProfileDraft(existing?.Id, name.Text,
                    host.Text, portValue, user.Text,
                    password.Visibility == Visibility.Visible ? password.Password : visiblePassword.Text,
                    false, defaultDatabase.Text,
                    sslMode.SelectedItem as string ?? "Preferred"));
                _connectionStatuses.Remove(saved.Id);
                await RefreshConnectionsAsync();
                SetStatus("连接已保存。", "");
                dialog.DialogResult = true;
            }
            catch ( Exception exception ) { error.Text = exception.Message; }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    private async Task OpenConnectionEditorAsync(MySqlConnectionProfile? existing)
    {
        try
        {
            await EditConnectionAsync(existing);
        }
        catch ( Exception error )
        {
            MessageBox.Show(this, "连接窗口无法打开：" + error.Message, "MySQL Schema",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void AddEditorField(Panel panel, string label, FrameworkElement editor)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 10, 0, 5),
            Foreground = AppResources.GetRequired<Brush>("ToolboxInk"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(editor);
    }

    private async Task DeleteConnectionAsync(ConnectionGridRow row)
    {
        var backupCount = await _workspace.CountSnapshotsForConnectionAsync(row.Profile.Id);
        var impact = backupCount > 0
            ? $"此连接关联 {backupCount} 份历史结构快照。删除连接只删除连接配置，历史快照仍可查看和对比。"
            : "删除连接配置不会删除其他数据。";
        var answer = MessageBox.Show(this, $"确定删除连接“{row.Name}”吗？{impact}", "删除连接",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if ( answer != MessageBoxResult.Yes ) return;
        await _workspace.DeleteConnectionAsync(row.Profile.Id);
        _connectionStatuses.Remove(row.Profile.Id);
        await RefreshConnectionsAsync();
        SetStatus("连接已删除。", "");
    }

    private async Task TestConnectionAsync(MySqlConnectionProfile profile)
    {
        SetConnectionStatus(profile.Id, new ConnectionStatusInfo("测试中", "Testing"));
        SetStatus($"正在测试“{profile.Name}”…", "");
        try
        {
            var result = await _workspace.TestConnectionAsync(profile.Id);
            if ( result.Success )
                SetConnectionStatus(profile.Id, new ConnectionStatusInfo("连接成功", "Success", result.ServerVersion));
            else
                SetConnectionStatus(profile.Id, new ConnectionStatusInfo("连接失败", "Failure", result.Message));
            SetStatus(result.Success ? $"“{profile.Name}”连接成功。" : $"“{profile.Name}”连接失败：{result.Message}", "");
        }
        catch ( Exception error )
        {
            SetConnectionStatus(profile.Id, new ConnectionStatusInfo("连接失败", "Failure", error.Message));
            SetStatus($"“{profile.Name}”连接失败：{error.Message}", "");
        }
    }

    private void SetConnectionStatus(string id, ConnectionStatusInfo status)
    {
        _connectionStatuses[id] = status;
        var row = _connections.FirstOrDefault(item => item.Profile.Id == id);
        if ( row is not null )
        {
            var index = _connections.IndexOf(row);
            _connections[index] = new ConnectionGridRow(row.Profile, status);
            if ( _connectionsGrid?.SelectedItem is ConnectionGridRow selected && selected.Profile.Id == id )
                _connectionsGrid.SelectedItem = _connections[index];
        }
        _connectionsGrid?.Items.Refresh();
        ApplyConnectionFilter();
    }

    private async Task CopyConnectionAsync(ConnectionGridRow row)
    {
        try
        {
            var password = await _workspace.GetConnectionPasswordAsync(row.Profile.Id);
            var content = string.Join(Environment.NewLine,
                $"连接名称：{row.Name}", $"主机：{row.Host}", $"端口：{row.Port}",
                $"用户名：{row.UserName}", $"密码：{password}",
                $"默认数据库：{row.Profile.DefaultDatabase ?? ""}", $"连接状态：{row.Status}");
            Clipboard.SetText(content);
            SetStatus($"已复制“{row.Name}”的连接属性（包含明文密码）。", "");
        }
        catch ( Exception error )
        {
            SetStatus("复制失败：" + error.Message, "");
        }
    }

    private async Task ShowConnectionDetailsAsync(ConnectionGridRow row)
    {
        try
        {
            var password = await _workspace.GetConnectionPasswordAsync(row.Profile.Id);
            var dialog = new Window
            {
                Title = "连接详情",
                Width = 500,
                SizeToContent = SizeToContent.Height,
                MinHeight = 400,
                MaxHeight = 700,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                Background = Brushes.White
            };
            var layout = new Grid { Margin = new Thickness(22) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var values = new (string Label, string Value)[]
            {
                ("连接名称", row.Name), ("主机", row.Host), ("端口", row.Port.ToString()),
                ("用户名", row.UserName)
            };
            var rowIndex = 0;
            foreach ( var pair in values )
            {
                AddDetailField(layout, pair.Label, pair.Value, rowIndex++);
            }
            var passwordLabel = DetailLabel("密码");
            Grid.SetRow(passwordLabel, rowIndex);
            Grid.SetColumn(passwordLabel, 0);
            layout.Children.Add(passwordLabel);
            var passwordLine = new Grid();
            passwordLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passwordLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var passwordBox = Input(password, double.NaN);
            passwordBox.IsReadOnly = true;
            passwordBox.Height = 34;
            passwordBox.Visibility = Visibility.Collapsed;
            var maskedPassword = Input(new string('•', Math.Max(8, password.Length)), double.NaN);
            maskedPassword.IsReadOnly = true;
            maskedPassword.Height = 34;
            var toggle = ConnectionButton("显示", () => { }, ConnectionButtonTone.Neutral);
            toggle.Margin = new Thickness(6, 0, 0, 0);
            toggle.Click += (_, _) =>
            {
                var show = passwordBox.Visibility == Visibility.Collapsed;
                passwordBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                maskedPassword.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                toggle.Content = show ? "隐藏" : "显示";
            };
            passwordLine.Children.Add(maskedPassword);
            passwordBox.SetValue(Grid.ColumnProperty, 0);
            passwordLine.Children.Add(passwordBox);
            Grid.SetColumn(toggle, 1);
            passwordLine.Children.Add(toggle);
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(passwordLine, rowIndex++);
            Grid.SetColumn(passwordLine, 1);
            layout.Children.Add(passwordLine);
            AddDetailField(layout, "默认数据库", row.Profile.DefaultDatabase ?? "—", rowIndex++);
            AddDetailField(layout, "连接状态", row.StatusDetails is { Length: > 0 } details
                ? $"{row.Status}：{details}" : row.Status, rowIndex++);
            var button = ConnectionButton("关闭", dialog.Close, ConnectionButtonTone.Neutral);
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(button, rowIndex);
            Grid.SetColumn(button, 1);
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.Children.Add(button);
            dialog.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = layout };
            dialog.ShowDialog();
        }
        catch ( Exception error )
        {
            SetStatus("读取连接详情失败：" + error.Message, "");
        }
    }

    private static void AddDetailField(Grid layout, string label, string value, int row)
    {
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var name = DetailLabel(label);
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        layout.Children.Add(name);
        var input = Input(value, double.NaN);
        input.IsReadOnly = true;
        input.Height = 34;
        input.Margin = new Thickness(0, 5, 0, 5);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        layout.Children.Add(input);
    }

    private static TextBlock DetailLabel(string label) => new()
    {
        Text = label,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = AppResources.GetRequired<Brush>("ToolboxMuted"),
        Margin = new Thickness(0, 5, 12, 5)
    };
}
