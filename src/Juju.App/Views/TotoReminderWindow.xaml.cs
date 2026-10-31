using System.Collections.ObjectModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Juju.Platform.Windows;
using Juju.Tools.Toto.Application;
using Juju.Tools.Toto.Application.Items;
using Juju.App.Presentation.Toto;
using DataGrid = System.Windows.Controls.DataGrid;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using SelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using Brush = System.Windows.Media.Brush;
using BrushConverter = System.Windows.Media.BrushConverter;

namespace Juju.App.Views;

public partial class TotoReminderWindow : Window
{
    private readonly ObservableCollection<TotoReminderRow> _singleItems = [];
    private readonly ObservableCollection<TotoReminderRow> _activeItems = [];
    private readonly ObservableCollection<TotoReminderRow> _completedItems = [];
    private readonly bool _reviewMode;
    private bool _syncingSelection;
    private bool _notified;

    internal TotoReminderWindow(string title, IReadOnlyList<TotoItemSnapshot> items, bool highlightTodayGreen = false,
        IReadOnlyList<TotoItemSnapshot>? completedItems = null, bool highlightTodayRed = false)
    {
        Title = title;
        InitializeComponent();
        DataContext = this;
        var reviewMode = completedItems is not null;
        _reviewMode = reviewMode;
        TodayTasks.Visibility = reviewMode ? Visibility.Collapsed : Visibility.Visible;
        ReviewScroll.Visibility = reviewMode ? Visibility.Visible : Visibility.Collapsed;

        var orderedActive = reviewMode || highlightTodayGreen
            ? SortReviewItems(items)
            : items;
        if (reviewMode)
        {
            foreach (var item in orderedActive)
                _activeItems.Add(new TotoReminderRow(item, IsToday(item.PlannedAt) && highlightTodayRed
                    ? "#EF4444" : null));
            foreach (var item in completedItems!) _completedItems.Add(new TotoReminderRow(item, null));
            ActiveCount.Text = $"{_activeItems.Count} 项";
            CompletedCount.Text = $"{_completedItems.Count} 项";
            ActiveReviewTasks.Height = TableHeight(_activeItems.Count);
            CompletedTasks.Height = TableHeight(_completedItems.Count);
        }
        else
        {
            foreach (var item in orderedActive)
                _singleItems.Add(new TotoReminderRow(item, IsToday(item.PlannedAt) && highlightTodayGreen
                    ? "#159A68" : null));
            TodayTasks.ItemsSource = SingleItems;
        }

        Loaded += (_, _) => FocusFirstRow();
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        ContentRendered += (_, _) =>
        {
            if (_notified) return;
            _notified = true;
            SystemSounds.Exclamation.Play();
            var flash = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                Flags = 3, Count = 3
            };
            FlashWindowEx(ref flash);
        };
    }

    public ObservableCollection<TotoReminderRow> SingleItems => _singleItems;
    public ObservableCollection<TotoReminderRow> ActiveReviewItems => _activeItems;
    public ObservableCollection<TotoReminderRow> CompletedItems => _completedItems;

    private void FocusFirstRow()
    {
        if (!_reviewMode)
        {
            TodayTasks.SelectedIndex = _singleItems.Count > 0 ? 0 : -1;
            FocusGrid(TodayTasks);
            return;
        }
        if (_activeItems.Count > 0) ActiveReviewTasks.SelectedIndex = 0;
        else if (_completedItems.Count > 0) CompletedTasks.SelectedIndex = 0;
        FocusGrid(_activeItems.Count > 0 || _completedItems.Count == 0 ? ActiveReviewTasks : CompletedTasks);
    }

    private void FocusGrid(DataGrid grid)
    {
        if (grid.SelectedItem is { } selected && grid.Columns.Count > 0)
        {
            grid.CurrentCell = new DataGridCellInfo(selected, grid.Columns[0]);
            grid.ScrollIntoView(selected, grid.Columns[0]);
        }
        grid.Focus();
        Keyboard.Focus(grid);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Left or Key.Right)
        {
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down)) return;
        if (!_reviewMode)
        {
            MoveWithinGrid(TodayTasks, _singleItems.Count, e.Key);
            if (_singleItems.Count > 0) e.Handled = true;
            return;
        }
        MoveAcrossReviewLists(e.Key);
        if (_activeItems.Count + _completedItems.Count > 0) e.Handled = true;
    }

    private void MoveWithinGrid(DataGrid grid, int count, Key key)
    {
        if (count == 0) return;
        var index = grid.SelectedIndex;
        var next = index < 0 ? (key == Key.Down ? 0 : count - 1) :
            key == Key.Down ? (index + 1) % count : (index - 1 + count) % count;
        grid.SelectedIndex = next;
        if (grid.SelectedItem is not null) FocusGrid(grid);
    }

    private void MoveAcrossReviewLists(Key key)
    {
        var total = _activeItems.Count + _completedItems.Count;
        if (total == 0) return;
        var index = ActiveReviewTasks.SelectedIndex >= 0 ? ActiveReviewTasks.SelectedIndex :
            CompletedTasks.SelectedIndex >= 0 ? _activeItems.Count + CompletedTasks.SelectedIndex : -1;
        var next = index < 0 ? (key == Key.Down ? 0 : total - 1) :
            key == Key.Down ? (index + 1) % total : (index - 1 + total) % total;
        _syncingSelection = true;
        if (next < _activeItems.Count)
        {
            ActiveReviewTasks.SelectedIndex = next;
            CompletedTasks.SelectedItem = null;
            FocusGrid(ActiveReviewTasks);
        }
        else
        {
            ActiveReviewTasks.SelectedItem = null;
            CompletedTasks.SelectedIndex = next - _activeItems.Count;
            FocusGrid(CompletedTasks);
        }
        _syncingSelection = false;
    }

    private void ReviewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        if (ReferenceEquals(sender, ActiveReviewTasks) && ActiveReviewTasks.SelectedItem is not null)
            CompletedTasks.SelectedItem = null;
        else if (ReferenceEquals(sender, CompletedTasks) && CompletedTasks.SelectedItem is not null)
            ActiveReviewTasks.SelectedItem = null;
        _syncingSelection = false;
    }

    private void ReviewScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ReviewScroll.ExtentHeight <= ReviewScroll.ViewportHeight) return;
        ReviewScroll.ScrollToVerticalOffset(ReviewScroll.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static IEnumerable<TotoItemSnapshot> SortReviewItems(IEnumerable<TotoItemSnapshot> items) => items
        .OrderBy(item => item.PlannedAt is null)
        .ThenByDescending(item => item.PlannedAt)
        .ThenBy(item => item.CreatedAt)
        .ThenBy(item => item.CreatedSeq);

    private static bool IsToday(DateTime? value) => value?.Date == DateTime.Today;

    private static double TableHeight(int count) => 40 + count * 45 + 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}

public sealed class TotoReminderRow
{
    private static readonly Brush Normal = (Brush)new BrushConverter().ConvertFromString("#25344D")!;

    internal TotoReminderRow(TotoItemSnapshot item, string? emphasis)
    {
        Content = item.Content;
        PlannedText = item.PlannedAt?.ToString(TotoDateTimeFormatter.DisplayFormat) ?? "—";
        RemindText = item.RemindAt?.ToString(TotoDateTimeFormatter.DisplayFormat) ?? "—";
        EndedText = item.EndedAt?.ToString(TotoDateTimeFormatter.DisplayFormat) ?? "—";
        ContentForeground = emphasis is null ? Normal :
            (Brush)new BrushConverter().ConvertFromString(emphasis)!;
        PlanForeground = item.PlannedAt is null ?
            (Brush)new BrushConverter().ConvertFromString("#93A0B2")! : ContentForeground;
    }

    public string Content { get; }
    public string PlannedText { get; }
    public string RemindText { get; }
    public string EndedText { get; }
    public Brush ContentForeground { get; }
    public Brush PlanForeground { get; }
    public Brush PlainForeground => Normal;
}
