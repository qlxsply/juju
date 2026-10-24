using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Juju.Platform.Windows;
using Toto.App;
using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Juju.App.Views;

internal enum TotoItemWindowMode { Add, Edit, Complete, Cancel, Detail }

public partial class TotoItemWindow : Window
{
    private readonly TotoToolHost _host;
    private readonly int _defaultMinutes;
    private readonly TotoItemWindowMode _mode;
    private readonly TodoItem? _item;
    private bool _initializingFields;
    private bool _saving;

    internal TotoItemWindow(TotoToolHost host, TotoItemWindowMode mode, TodoItem? item = null)
    {
        _host = host;
        _defaultMinutes = int.TryParse(host.ToolSettings.GetValueOrDefault("default_remind_minutes"), out var minutes)
            ? minutes : 5;
        _mode = mode;
        _item = item;
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        QuickPanel.Visibility = mode == TotoItemWindowMode.Add ? Visibility.Visible : Visibility.Collapsed;
        EditPanel.Visibility = mode == TotoItemWindowMode.Edit ? Visibility.Visible : Visibility.Collapsed;
        EndPanel.Visibility = mode is TotoItemWindowMode.Complete or TotoItemWindowMode.Cancel
            ? Visibility.Visible : Visibility.Collapsed;
        DetailPanel.Visibility = mode == TotoItemWindowMode.Detail ? Visibility.Visible : Visibility.Collapsed;
        Title = mode switch
        {
            TotoItemWindowMode.Add => "新增事项", TotoItemWindowMode.Edit => "编辑事项",
            TotoItemWindowMode.Complete => "完成事项", TotoItemWindowMode.Cancel => "取消事项",
            _ => item?.Status == ItemStatus.Active ? "事项详情" : "历史事项详情"
        };
        if (item is not null)
        {
            if (mode == TotoItemWindowMode.Edit) LoadEdit(item);
            if (mode is TotoItemWindowMode.Complete or TotoItemWindowMode.Cancel)
            {
                EndQuestion.Text = $"确认“{(mode == TotoItemWindowMode.Complete ? "已完成" : "已取消")}”：{item.Content}";
                EndNoteLabel.Text = mode == TotoItemWindowMode.Complete ? "完成备注" : "取消备注";
                EndNote.Text = item.Note;
            }
            if (mode == TotoItemWindowMode.Detail) LoadDetail(item);
        }
        _initializingFields = true;
        PlanHour.ItemsSource = RemindHour.ItemsSource = Enumerable.Range(0, 24).Select(value => value.ToString("D2")).ToArray();
        PlanMinute.ItemsSource = RemindMinute.ItemsSource = Enumerable.Range(0, 60).Select(value => value.ToString("D2")).ToArray();
        if (mode == TotoItemWindowMode.Add)
        {
            var now = DateTime.Now;
            PlanDate.SelectedDate = now.Date;
            PlanHour.SelectedItem = now.ToString("HH");
            PlanMinute.SelectedItem = now.ToString("mm");
            var remind = now.AddMinutes(-_defaultMinutes);
            RemindDate.SelectedDate = remind.Date;
            RemindHour.SelectedItem = remind.ToString("HH");
            RemindMinute.SelectedItem = remind.ToString("mm");
        }
        else if (mode == TotoItemWindowMode.Edit && item is not null)
        {
            SetTimePickers(PlanHour, PlanMinute, item.PlannedAt);
            SetTimePickers(RemindHour, RemindMinute, item.RemindAt);
        }
        _initializingFields = false;
        if (mode == TotoItemWindowMode.Detail)
        {
            SaveButton.Content = "关闭";
            CancelButton.Visibility = Visibility.Collapsed;
        }
        if (mode is TotoItemWindowMode.Complete or TotoItemWindowMode.Cancel) SaveButton.Content = "确认";
        Loaded += (_, _) =>
        {
            if (mode == TotoItemWindowMode.Add) QuickInput.Focus();
            else if (mode == TotoItemWindowMode.Edit) ContentInput.Focus();
            else if (mode is TotoItemWindowMode.Complete or TotoItemWindowMode.Cancel) EndNote.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    private void LoadEdit(TodoItem item)
    {
        _initializingFields = true;
        ContentInput.Text = item.Content;
        UsePlan.IsChecked = item.PlannedAt is not null;
        UseRemind.IsChecked = item.RemindAt is not null;
        PlanDate.SelectedDate = item.PlannedAt?.Date;
        RemindDate.SelectedDate = item.RemindAt?.Date;
        NoteInput.Text = item.Note;
        PlanFields.IsEnabled = UsePlan.IsChecked == true;
        RemindFields.IsEnabled = UseRemind.IsChecked == true;
        _initializingFields = false;
    }

    private void LoadDetail(TodoItem item)
    {
        foreach (var (label, value) in new[]
                 {
                     ("事项内容", item.Content), ("计划时间", Format(item.PlannedAt)),
                     ("提醒时间", Format(item.RemindAt)), ("创建时间", Format(item.CreatedAt)),
                     (item.Status == ItemStatus.Active ? "提醒状态" : "结束状态",
                         item.Status == ItemStatus.Active ? item.ReminderStatus switch
                         {
                             ReminderStatus.Pending => "未提醒", ReminderStatus.Reminded => "已提醒", _ => "无提醒"
                         } : item.Status == ItemStatus.Completed ? "已完成" : "已取消"),
                     (item.Status == ItemStatus.Active ? "响铃时间" : "结束时间",
                         Format(item.Status == ItemStatus.Active ? item.RemindedAt : item.EndedAt)),
                     ("备注", string.IsNullOrWhiteSpace(item.Note) ? "—" : item.Note)
                 })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)Application.Current.Resources["ToolboxMuted"] });
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["ToolboxInk"] };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            DetailPanel.Children.Add(row);
        }
    }

    private static string Format(DateTime? time) => time?.ToString(DateTimeText.DisplayFormat) ?? "—";

    private static void SetTimePickers(ComboBox hour, ComboBox minute, DateTime? value)
    {
        if (value is null)
        {
            hour.SelectedItem = null;
            minute.SelectedItem = null;
            return;
        }
        var time = value.Value.ToString("HH:mm");
        hour.SelectedItem = time[..2];
        minute.SelectedItem = time[3..];
    }

    private void QuickInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (QuickPreview is null) return;
        if (string.IsNullOrWhiteSpace(QuickInput.Text))
        {
            QuickPreview.Text = "输入事项内容及计划时间";
            QuickPreview.Foreground = (Brush)Application.Current.Resources["ToolboxMuted"];
            return;
        }
        var valid = QuickItemParser.TryParse(QuickInput.Text, DefaultMinutes(), out var name, out var plan,
            out var remind, out var error);
        QuickPreview.Text = valid ? $"{name}　{(plan is null ? "无计划时间" : Format(plan))}" +
            (remind is null ? "" : $"　提醒 {Format(remind)}") : error;
        QuickPreview.Foreground = valid ? (Brush)Application.Current.Resources["ToolboxInk"] : Brushes.IndianRed;
    }

    private int DefaultMinutes() => _defaultMinutes;

    private void UsePlan_Changed(object sender, RoutedEventArgs e)
    {
        if (PlanFields is null) return;
        PlanFields.IsEnabled = UsePlan.IsChecked == true;
        UpdateReminderFromPlan();
    }

    private void UseRemind_Changed(object sender, RoutedEventArgs e)
    {
        if (RemindFields is not null) RemindFields.IsEnabled = UseRemind.IsChecked == true;
    }

    private void PlanDateTime_Changed(object sender, RoutedEventArgs e) => UpdateReminderFromPlan();

    private void UpdateReminderFromPlan()
    {
        if (_initializingFields || _mode != TotoItemWindowMode.Edit || PlanDate is null ||
            UsePlan?.IsChecked != true || PlanDate.SelectedDate is not { } date ||
            PlanHour.SelectedItem is not string hour || PlanMinute.SelectedItem is not string minute ||
            !TimeOnly.TryParseExact(hour + ":" + minute, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time)) return;
        var remind = date.Date.Add(time.ToTimeSpan()).AddMinutes(-DefaultMinutes());
        UseRemind.IsChecked = true;
        RemindDate.SelectedDate = remind.Date;
        RemindHour.SelectedItem = remind.ToString("HH");
        RemindMinute.SelectedItem = remind.ToString("mm");
    }

    private static DateTime? ReadTime(bool enabled, DatePicker date, ComboBox hour, ComboBox minute)
    {
        if (!enabled) return null;
        if (date.SelectedDate is not { } selected ||
            hour.SelectedItem is not string hourValue || minute.SelectedItem is not string minuteValue ||
            !TimeOnly.TryParseExact(hourValue + ":" + minuteValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed))
            throw new InvalidOperationException("请选择有效日期并以 24 小时制 HH:mm 输入时间。");
        return selected.Date.Add(parsed.ToTimeSpan());
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == TotoItemWindowMode.Detail) { Close(); return; }
        if (_saving) return;
        _saving = true;
        try
        {
            switch (_mode)
            {
                case TotoItemWindowMode.Add:
                    if (!QuickItemParser.TryParse(QuickInput.Text, DefaultMinutes(), out var content,
                            out var plan, out var remind, out var error))
                        throw new InvalidOperationException(error);
                    _host.Items.Add(new TodoItem(Guid.NewGuid().ToString(), content, plan, remind, DateTime.Now, 0,
                        ItemStatus.Active, remind is null ? ReminderStatus.None : ReminderStatus.Pending,
                        null, null, ""));
                    break;
                case TotoItemWindowMode.Edit when _item is not null:
                    var text = ContentInput.Text.Trim();
                    if (text.Length == 0 || text.Contains('\n') || text.Contains('\r'))
                        throw new InvalidOperationException("事项内容不能为空且不能包含换行。");
                    var newPlan = ReadTime(UsePlan.IsChecked == true, PlanDate, PlanHour, PlanMinute);
                    var newRemind = ReadTime(UseRemind.IsChecked == true, RemindDate, RemindHour, RemindMinute);
                    var changed = newRemind != _item.RemindAt;
                    if (!_host.Items.Update(_item with
                        {
                            Content = text, PlannedAt = newPlan, RemindAt = newRemind,
                            ReminderStatus = newRemind is null ? ReminderStatus.None :
                                changed ? ReminderStatus.Pending : _item.ReminderStatus,
                            RemindedAt = changed || newRemind is null ? null : _item.RemindedAt,
                            Note = NoteInput.Text
                        })) throw new InvalidOperationException("事项不存在或已被处理。");
                    break;
                case TotoItemWindowMode.Complete or TotoItemWindowMode.Cancel when _item is not null:
                    var status = _mode == TotoItemWindowMode.Complete ? ItemStatus.Completed : ItemStatus.Cancelled;
                    if (!_host.Items.End(_item.Id, status, EndNote.Text, DateTime.Now))
                        throw new InvalidOperationException("事项不存在或已被处理。");
                    break;
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _saving = false; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
