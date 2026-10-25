using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Juju.App.Views;

public partial class TotoTimePicker : UserControl
{
    private bool _syncing;
    private string _hour = "00";

    public TotoTimePicker()
    {
        InitializeComponent();
        HourList.ItemsSource = Enumerable.Range(0, 24).Select(value => value.ToString("D2")).ToArray();
        MinuteList.ItemsSource = Enumerable.Range(0, 60).Select(value => value.ToString("D2")).ToArray();
        UpdateDisplay();
        TimePopup.Opened += (_, _) => ScrollSelectedIntoView();
    }

    public event EventHandler? TimeCommitted;

    public string? Time
    {
        get => _time;
        set
        {
            _time = Normalize(value);
            UpdateDisplay();
        }
    }

    private string? _time;

    private static string? Normalize(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;

    private void UpdateDisplay() => TimeText.Text = Time ?? "--:--";

    private void TimeButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimePopup.IsOpen)
        {
            TimePopup.IsOpen = false;
            return;
        }
        var parsed = TimeOnly.TryParseExact(Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var current) ? current : new TimeOnly(0, 0);
        _hour = parsed.ToString("HH", CultureInfo.InvariantCulture);
        _syncing = true;
        HourList.SelectedItem = _hour;
        MinuteList.SelectedItem = parsed.ToString("mm", CultureInfo.InvariantCulture);
        _syncing = false;
        TimePopup.IsOpen = true;
    }

    private void Hour_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || HourList.SelectedItem is not string hour) return;
        _hour = hour;
        // Selecting an hour only changes the draft. The popup remains open until a minute is chosen.
    }

    private void Minute_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || MinuteList.SelectedItem is not string minute) return;
        CommitMinute(minute);
    }

    private void Minute_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TimePopup.IsOpen) return;
        var item = ItemsControl.ContainerFromElement(MinuteList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.Content is string minute) CommitMinute(minute);
    }

    private void CommitMinute(string minute)
    {
        Time = _hour + ":" + minute;
        TimePopup.IsOpen = false;
        TimeCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void ScrollSelectedIntoView()
    {
        if (HourList.SelectedItem is not null) HourList.ScrollIntoView(HourList.SelectedItem);
        if (MinuteList.SelectedItem is not null) MinuteList.ScrollIntoView(MinuteList.SelectedItem);
    }

    private void Picker_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        TimePopup.IsOpen = false;
    }
}
