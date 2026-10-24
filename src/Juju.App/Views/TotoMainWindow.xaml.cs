using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Juju.Platform.Windows;
using Toto.App;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using MessageBox = System.Windows.MessageBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Juju.App.Views;

public partial class TotoMainWindow : Window
{
    private readonly TotoToolHost _host;
    private readonly Action _openHistory;
    private readonly ObservableCollection<TotoItemRow> _activeItems = [];
    private bool _allowClose;

    public TotoMainWindow(TotoToolHost host, Action openHistory)
    {
        _host = host;
        _openHistory = openHistory;
        InitializeComponent();
        DataContext = this;
        PreviewKeyDown += HandleShortcut;
        Activated += (_, _) => { RefreshItems(); FocusItems(); };
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        RefreshItems();
        Loaded += (_, _) => FocusItems();
    }

    public ObservableCollection<TotoItemRow> ActiveItems => _activeItems;

    public void AllowShutdown() => _allowClose = true;

    public void FocusItems()
    {
        Tasks.Focus();
        Keyboard.Focus(Tasks);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    public void RefreshItems()
    {
        var selectedId = (Tasks.SelectedItem as TotoItemRow)?.Item.Id;
        _activeItems.Clear();
        foreach (var item in _host.Items.GetActive()) _activeItems.Add(new TotoItemRow(item));
        var selected = _activeItems.FirstOrDefault(item => item.Item.Id == selectedId) ?? _activeItems.FirstOrDefault();
        Tasks.SelectedItem = selected;
        if (selected is not null && Tasks.Columns.Count > 0)
        {
            Tasks.CurrentCell = new DataGridCellInfo(selected, Tasks.Columns[0]);
            Tasks.ScrollIntoView(selected);
        }
        ToolWindowChrome.SetStatusLeft(this, $"进行中：{_activeItems.Count} 项");
    }

    private TodoItem? SelectedItem => (Tasks.SelectedItem as TotoItemRow)?.Item;

    private void Tasks_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(Tasks, e.OriginalSource as DependencyObject) is DataGridRow &&
            SelectedItem is { } item) ShowDetail(item);
    }

    private void Tasks_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Up or Key.Down)
        {
            if (_activeItems.Count == 0) return;
            var index = Tasks.SelectedIndex;
            var next = index < 0 ? (e.Key == Key.Down ? 0 : _activeItems.Count - 1) :
                e.Key == Key.Down ? (index + 1) % _activeItems.Count :
                (index - 1 + _activeItems.Count) % _activeItems.Count;
            Tasks.SelectedIndex = next;
            if (Tasks.SelectedItem is { } selected)
            {
                Tasks.CurrentCell = new DataGridCellInfo(selected, Tasks.Columns[0]);
                Tasks.ScrollIntoView(selected, Tasks.Columns[0]);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && SelectedItem is { } item)
        {
            e.Handled = true;
            ShowDetail(item);
        }
    }

    private void HandleShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            Close();
            return;
        }
        if (e.IsRepeat || !TotoShortcutKey.TryGet(e, out var pressed)) return;
        var shortcuts = _host.ToolSettings;
        var action = new[]
        {
            "shortcut_add", "shortcut_history", "shortcut_refresh", "shortcut_detail", "shortcut_edit",
            "shortcut_complete", "shortcut_cancel"
        }.FirstOrDefault(key => WindowShortcut.TryNormalize(shortcuts.GetValueOrDefault(key), out var configured) &&
                                string.Equals(pressed, configured, StringComparison.OrdinalIgnoreCase));
        if (action is null) return;
        e.Handled = true;
        switch (action)
        {
            case "shortcut_add": ShowAdd(); break;
            case "shortcut_history": _openHistory(); break;
            case "shortcut_refresh": RefreshItems(); break;
            case "shortcut_detail": if (SelectedItem is { } detail) ShowDetail(detail); break;
            case "shortcut_edit": if (SelectedItem is { } edit) ShowEdit(edit); break;
            case "shortcut_complete": EndSelected(ItemStatus.Completed); break;
            case "shortcut_cancel": EndSelected(ItemStatus.Cancelled); break;
        }
    }

    public void ShowAdd()
    {
        if (new TotoItemWindow(_host, TotoItemWindowMode.Add) { Owner = this }.ShowDialog() == true)
            OnItemChanged();
    }

    private void ShowEdit(TodoItem item)
    {
        if (new TotoItemWindow(_host, TotoItemWindowMode.Edit, item) { Owner = this }.ShowDialog() == true)
            OnItemChanged();
    }

    private void ShowDetail(TodoItem item) =>
        new TotoItemWindow(_host, TotoItemWindowMode.Detail, item) { Owner = this }.ShowDialog();

    private void EndSelected(ItemStatus status)
    {
        if (SelectedItem is not { } item) return;
        if (new TotoItemWindow(_host, status == ItemStatus.Completed ? TotoItemWindowMode.Complete : TotoItemWindowMode.Cancel,
                item) { Owner = this }.ShowDialog() == true) OnItemChanged();
    }

    private void OnItemChanged()
    {
        RefreshItems();
        _host.Reschedule();
    }
}

public sealed class TotoItemRow
{
    internal TotoItemRow(TodoItem item) => Item = item;
    internal TodoItem Item { get; }
    public string Content => Item.Content;
    public string PlannedText => Item.PlannedAt?.ToString(DateTimeText.DisplayFormat) ?? "—";
    public string RemindText => Item.RemindAt?.ToString(DateTimeText.DisplayFormat) ?? "—";
    public string CreatedText => Item.CreatedAt.ToString(DateTimeText.DisplayFormat);
    public string Note => string.IsNullOrWhiteSpace(Item.Note) ? "—" : Item.Note;
    public string StatusText => Item.ReminderStatus switch
    {
        ReminderStatus.Pending => "未提醒", ReminderStatus.Reminded => "已提醒", _ => "无提醒"
    };
    public Brush StatusForeground => new System.Windows.Media.SolidColorBrush(Item.ReminderStatus switch
    {
        ReminderStatus.Pending => Color.FromRgb(38, 133, 99),
        ReminderStatus.Reminded => Color.FromRgb(69, 102, 199),
        _ => Color.FromRgb(138, 149, 165)
    });
}
