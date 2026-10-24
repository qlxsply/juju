using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Juju.Platform.Windows;
using Toto.App;
using Toto.App.Data;
using Toto.App.Domain;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Juju.App.Views;

public partial class TotoHistoryWindow : Window
{
    private readonly TotoToolHost _host;
    private readonly ObservableCollection<TotoHistoryRow> _historyItems = [];
    private QueryCriteria _criteria = new();
    private int _page = 1;
    private int _total;
    private bool _updatingPeriod;

    internal TotoHistoryWindow(TotoToolHost host)
    {
        _host = host;
        InitializeComponent();
        DataContext = this;
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        StatusFilter.ItemsSource = new[] { "全部状态", "已完成", "已取消" };
        StatusFilter.SelectedIndex = 0;
        PeriodPreset.ItemsSource = new[] { "不限", "本周", "上一周", "本月", "上一月", "本年", "上一年", "自定义" };
        PeriodPreset.SelectedIndex = 0;
        PageSize.ItemsSource = new[] { 10, 20, 50, 100, 200, 500 };
        PageSize.SelectedItem = 10;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        Loaded += (_, _) => FocusHistoryRows();
        LoadPage();
    }

    public ObservableCollection<TotoHistoryRow> HistoryItems => _historyItems;
    private int MaxPage => Math.Max(1, (int)Math.Ceiling(_total / (double)SelectedPageSize));
    private int SelectedPageSize => PageSize.SelectedItem is int size ? size : 10;

    private void LoadPage()
    {
        var page = _host.Items.GetHistory(_criteria, _page, SelectedPageSize);
        _total = page.TotalCount;
        if (_page > MaxPage)
        {
            _page = MaxPage;
            page = _host.Items.GetHistory(_criteria, _page, SelectedPageSize);
        }
        _historyItems.Clear();
        foreach (var item in page.Items) _historyItems.Add(new TotoHistoryRow(item));
        History.SelectedItem = _historyItems.FirstOrDefault();
        if (History.SelectedItem is { } first && History.Columns.Count > 0)
        {
            History.CurrentCell = new DataGridCellInfo(first, History.Columns[0]);
            History.ScrollIntoView(first);
        }
        FocusHistoryRows();
        PageLabel.Text = $"共 {_total} 条，第 {_page}/{MaxPage} 页";
        ToolWindowChrome.SetStatusLeft(this, $"历史事项：{_total} 条");
    }

    private void FocusHistoryRows()
    {
        if (!IsVisible || History.SelectedItem is null) return;
        History.Focus();
        Keyboard.Focus(History);
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (FromDate.SelectedDate is { } from && ToDate.SelectedDate is { } to && from.Date > to.Date)
        {
            System.Windows.MessageBox.Show(this, "结束日期的起始值不能晚于结束值。", "历史事项");
            return;
        }
        _criteria = new QueryCriteria
        {
            SearchText = SearchFilter.Text.Trim(),
            Status = StatusFilter.SelectedIndex switch { 1 => ItemStatus.Completed, 2 => ItemStatus.Cancelled, _ => null },
            EndedFrom = FromDate.SelectedDate?.Date,
            EndedTo = ToDate.SelectedDate?.Date.AddDays(1)
        };
        _page = 1;
        LoadPage();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        SearchFilter.Clear();
        StatusFilter.SelectedIndex = 0;
        _updatingPeriod = true;
        PeriodPreset.SelectedIndex = 0;
        FromDate.SelectedDate = null;
        ToDate.SelectedDate = null;
        _updatingPeriod = false;
        _criteria = new QueryCriteria();
        _page = 1;
        LoadPage();
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (History is null) return;
        _page = 1;
        LoadPage();
    }

    private void PeriodPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FromDate is null || _updatingPeriod) return;
        if (PeriodPreset.SelectedItem as string == "自定义") return;
        var (from, to) = PeriodBounds(PeriodPreset.SelectedItem as string, DateTime.Today);
        _updatingPeriod = true;
        FromDate.SelectedDate = from;
        ToDate.SelectedDate = to;
        _updatingPeriod = false;
    }

    private void RangeDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPeriod || PeriodPreset is null) return;
        var from = FromDate.SelectedDate?.Date;
        var to = ToDate.SelectedDate?.Date;
        var matching = from is null && to is null ? 0 : 7;
        if (matching == 7)
        {
            var presets = new[] { "本周", "上一周", "本月", "上一月", "本年", "上一年" };
            for (var i = 0; i < presets.Length; i++)
            {
                var bounds = PeriodBounds(presets[i], DateTime.Today);
                if (bounds.From == from && bounds.To == to) { matching = i + 1; break; }
            }
        }
        _updatingPeriod = true;
        PeriodPreset.SelectedIndex = matching;
        _updatingPeriod = false;
    }

    private static (DateTime? From, DateTime? To) PeriodBounds(string? period, DateTime today)
    {
        var monday = today.Date.AddDays(-((int)today.DayOfWeek + 6) % 7);
        return period switch
        {
            "本周" => (monday, monday.AddDays(6)),
            "上一周" => (monday.AddDays(-7), monday.AddDays(-1)),
            "本月" => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
            "上一月" => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            "本年" => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            "上一年" => (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31)),
            _ => (null, null)
        };
    }

    private void SearchFilter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Search_Click(sender, e);
    }

    private void History_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key is Key.Left or Key.Right)
        {
            e.Handled = true;
            return;
        }
        if (e.Key is Key.Up or Key.Down)
        {
            if (_historyItems.Count == 0) return;
            var index = History.SelectedIndex;
            var next = index < 0 ? (e.Key == Key.Down ? 0 : _historyItems.Count - 1) :
                e.Key == Key.Down ? (index + 1) % _historyItems.Count :
                (index - 1 + _historyItems.Count) % _historyItems.Count;
            History.SelectedIndex = next;
            if (History.SelectedItem is { } selected)
            {
                History.CurrentCell = new DataGridCellInfo(selected, History.Columns[0]);
                History.ScrollIntoView(selected, History.Columns[0]);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && History.SelectedItem is TotoHistoryRow row)
        {
            e.Handled = true;
            new TotoItemWindow(_host, TotoItemWindowMode.Detail, row.Item) { Owner = this }.ShowDialog();
        }
    }

    private void First_Click(object sender, RoutedEventArgs e) { _page = 1; LoadPage(); }
    private void Previous_Click(object sender, RoutedEventArgs e) { _page = Math.Max(1, _page - 1); LoadPage(); }
    private void Next_Click(object sender, RoutedEventArgs e) { _page = Math.Min(MaxPage, _page + 1); LoadPage(); }
    private void Last_Click(object sender, RoutedEventArgs e) { _page = MaxPage; LoadPage(); }

    private void History_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(History, e.OriginalSource as DependencyObject) is DataGridRow &&
            History.SelectedItem is TotoHistoryRow row)
            new TotoItemWindow(_host, TotoItemWindowMode.Detail, row.Item) { Owner = this }.ShowDialog();
    }
}

public sealed class TotoHistoryRow
{
    internal TotoHistoryRow(TodoItem item) => Item = item;
    internal TodoItem Item { get; }
    public string Content => Item.Content;
    public string PlannedText => Item.PlannedAt?.ToString(DateTimeText.DisplayFormat) ?? "—";
    public string RemindText => Item.RemindAt?.ToString(DateTimeText.DisplayFormat) ?? "—";
    public string CreatedText => Item.CreatedAt.ToString(DateTimeText.DisplayFormat);
    public string EndedText => Item.EndedAt?.ToString(DateTimeText.DisplayFormat) ?? "—";
    public string StatusText => Item.Status == ItemStatus.Completed ? "已完成" : "已取消";
    public string Note => string.IsNullOrWhiteSpace(Item.Note) ? "—" : Item.Note;
}
