using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.App.Views;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;
using Toto.App;
using Toto.App.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Panel = System.Windows.Controls.Panel;

namespace Juju.App.Settings;

/// <summary>在同一 WPF 设置中心管理 Toto：每个独立字段即时提交，时段和日期等关联值用草稿弹窗。</summary>
public sealed class TotoSettingsSection : ISettingsSection
{
    private readonly TotoToolHost _toto;
    private readonly ISettingsService _application;
    private readonly IJsonToolSettingsService _jsonSettings;
    private readonly DataDirectoryChangeService _dataChanges;
    private readonly SettingsDialogService _dialogs;
    private readonly Action<string> _renderLaunchKey;
    private readonly Action<string> _renderGlobalKey;
    private readonly Action<string> _renderDirectory;
    private readonly TextBox _minutes;
    private readonly TotoTimePicker _dailyStart = new();
    private readonly TotoTimePicker _dailyEnd = new();
    private readonly CheckBox _weekdaysOnly = new();
    private readonly Dictionary<string, string> _shortcuts;
    private readonly Dictionary<string, Action<string>> _renderShortcuts = [];
    private readonly ComboBox _year = new() { Width = 112 };
    private readonly StackPanel _holidayRows = new();
    private readonly List<TotoHoliday> _calendarDays = [];
    private readonly HashSet<DateOnly> _selectedHolidays = [];
    private readonly TextBlock _calendarStatus = new() { FontSize = 11, Foreground = System.Windows.Media.Brushes.SlateGray };
    private readonly TextBlock _calendarCount = new() { FontSize = 11, Foreground = System.Windows.Media.Brushes.SlateGray };
    private readonly System.Windows.Controls.Button _deleteSelected = new() { Content = "删除选中", IsEnabled = false };
    private CheckBox? _selectAllHolidays;
    private bool _updatingCalendarSelection;

    public string Id => "toto";
    public string DisplayName => "Toto 日程提醒";
    public FrameworkElement View { get; }

    public TotoSettingsSection(TotoToolHost toto, ISettingsService application, IJsonToolSettingsService jsonSettings,
        DataDirectoryChangeService dataChanges, SettingsDialogService dialogs)
    {
        _toto = toto;
        _application = application;
        _jsonSettings = jsonSettings;
        _dataChanges = dataChanges;
        _dialogs = dialogs;
        var values = toto.ToolSettings;
        var page = SettingsViewFactory.Page();

        var entryRows = SettingsViewFactory.ShortcutGroup(page, "工具入口",
        [
            ("launcher", "主界面快捷键", application.Current.ToolKeys[nameof(ToolId.Toto)], (Action)EditLaunchKey),
            ("global", "全局快捷键", toto.Hotkey, (Action)EditGlobalHotkey)
        ]);
        _renderLaunchKey = entryRows["launcher"];
        _renderGlobalKey = entryRows["global"];

        var reminders = SettingsViewFactory.Group(page, "提醒设置");
        _minutes = SettingsViewFactory.Input(values.GetValueOrDefault("default_remind_minutes", "5"), 90);
        SettingsViewFactory.Row(reminders, "默认提前提醒分钟", "", _minutes);

        var reviews = SettingsViewFactory.Group(page, "每日事项盘点");
        _dailyStart.Time = values.GetValueOrDefault("daily_review_start_time");
        _dailyEnd.Time = values.GetValueOrDefault("daily_review_end_time");
        _dailyStart.TimeCommitted += (_, _) => SaveDailyReview();
        _dailyEnd.TimeCommitted += (_, _) => SaveDailyReview();
        SettingsViewFactory.Row(reviews, "开始盘点", "", ReviewTimeControl(_dailyStart));
        SettingsViewFactory.Row(reviews, "结束盘点", "", ReviewTimeControl(_dailyEnd));
        _weekdaysOnly = new CheckBox
        {
            Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center,
            IsChecked = values.GetValueOrDefault("daily_review_workdays") == "1",
            ToolTip = "仅工作日提醒"
        };
        _weekdaysOnly.Checked += (_, _) => SaveDailyReview();
        _weekdaysOnly.Unchecked += (_, _) => SaveDailyReview();
        SettingsViewFactory.Row(reviews, "仅工作日提醒", "", _weekdaysOnly);

        _shortcuts = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        var shortcutDefinitions = new[]
        {
            ("新增", "shortcut_add"), ("历史", "shortcut_history"),
            ("刷新", "shortcut_refresh"), ("详情", "shortcut_detail"), ("编辑", "shortcut_edit"),
            ("完成", "shortcut_complete"), ("取消", "shortcut_cancel")
        };
        var shortcutRenderers = SettingsViewFactory.ShortcutGroup(page, "快捷键",
            shortcutDefinitions.Select(item => (item.Item2, item.Item1, values.GetValueOrDefault(item.Item2, ""),
                (Action)(() => EditWindowShortcut(item.Item2, item.Item1)))));
        foreach (var (label, key) in shortcutDefinitions) _renderShortcuts[key] = shortcutRenderers[key];

        var storage = SettingsViewFactory.Group(page, "存储目录");
        _renderDirectory = SettingsViewFactory.DirectoryRow(storage, "Toto 数据目录",
            dataChanges.DisplayToolDirectory("toto"), EditDirectory,
            () => SettingsViewFactory.OpenDirectory(_dataChanges.DisplayToolDirectory("toto")));

        var calendar = SettingsViewFactory.Group(page, "节假日");
        for (var year = 2020; year <= toto.LatestCalendarYear; year++) _year.Items.Add(year);
        _year.SelectedItem = DateTime.Today.Year;
        _year.SelectionChanged += (_, _) => { if (_toto.CalendarReady) RefreshCalendar(); };
        BuildCalendarPanel((StackPanel)calendar.Child);
        View = page;
        if (toto.CalendarReady) RefreshCalendar();
    }

    /// <summary>本页所有可编辑值均各自提交或通过专用弹窗提交，不使用全局“保存更改”。</summary>
    public Task SaveAsync() => Task.CompletedTask;

    private void BuildCalendarPanel(StackPanel host)
    {
        var toolbar = new Grid { Margin = new Thickness(14, 12, 14, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var yearPicker = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        yearPicker.Children.Add(new TextBlock { Text = "年份", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["ToolboxInk"], VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0) });
        yearPicker.Children.Add(_year);
        toolbar.Children.Add(yearPicker);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var add = SettingsViewFactory.SoftButton("＋ 新增", (_, _) => EditHoliday(null));
        add.Background = (Brush)new BrushConverter().ConvertFromString("#6078E9")!;
        add.Foreground = Brushes.White;
        add.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#6078E9")!;
        add.Margin = new Thickness(0, 0, 7, 0);
        actions.Children.Add(add);
        _deleteSelected.Content = "删除选中";
        _deleteSelected.Style = (Style)Application.Current.Resources["ToolboxSoftButton"];
        _deleteSelected.Margin = new Thickness(0, 0, 7, 0);
        _deleteSelected.Click += (_, _) => DeleteHoliday();
        actions.Children.Add(_deleteSelected);
        var download = SettingsViewFactory.SoftButton("↓ 更新", async (_, _) => await DownloadCalendarAsync());
        actions.Children.Add(download);
        Grid.SetColumn(actions, 1);
        toolbar.Children.Add(actions);
        host.Children.Add(toolbar);

        var header = CalendarColumns();
        var headerBorder = new Border
        {
            Background = (Brush)new BrushConverter().ConvertFromString("#F6F8FC")!,
            BorderBrush = (Brush)Application.Current.Resources["ToolboxLine"],
            BorderThickness = new Thickness(0, 1, 0, 1), Child = header
        };
        var selectAll = new CheckBox { HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = "全选", IsThreeState = true };
        _selectAllHolidays = selectAll;
        selectAll.Checked += (_, _) =>
        {
            if (_updatingCalendarSelection) return;
            _selectedHolidays.Clear();
            _selectedHolidays.UnionWith(_calendarDays.Select(day => day.Date));
            RenderHolidayRows();
        };
        selectAll.Unchecked += (_, _) =>
        {
            if (_updatingCalendarSelection) return;
            _selectedHolidays.Clear();
            RenderHolidayRows();
        };
        selectAll.Indeterminate += (_, _) =>
        {
            if (_updatingCalendarSelection) return;
            _selectedHolidays.Clear();
            RenderHolidayRows();
        };
        Grid.SetColumn(selectAll, 0);
        header.Children.Add(selectAll);
        AddCalendarHeaderCell(header, "日期", 1);
        AddCalendarHeaderCell(header, "节假日名称", 2);
        AddCalendarHeaderCell(header, "日期类型", 3);
        AddCalendarHeaderCell(header, "", 4);
        host.Children.Add(headerBorder);

        var rowsBorder = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["ToolboxLine"], BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.White
        };
        var scroll = new ScrollViewer { Height = 248, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _holidayRows };
        rowsBorder.Child = scroll;
        host.Children.Add(rowsBorder);

        var footer = new Grid { Margin = new Thickness(14, 9, 14, 10) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(_calendarCount);
        _calendarStatus.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        Grid.SetColumn(_calendarStatus, 1);
        footer.Children.Add(_calendarStatus);
        host.Children.Add(footer);
        UpdateCalendarFooter();
    }

    private static Grid CalendarColumns()
    {
        var grid = new Grid { MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        return grid;
    }

    private static void AddCalendarHeaderCell(Grid grid, string text, int column)
    {
        var cell = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["ToolboxMuted"], VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0) };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static TextBlock CalendarCell(string text) => new()
    {
        Text = text, FontSize = 11, Foreground = (Brush)Application.Current.Resources["ToolboxInk"],
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0),
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private void RenderHolidayRows()
    {
        _holidayRows.Children.Clear();
        foreach (var day in _calendarDays)
        {
            var row = CalendarColumns();
            var selection = new CheckBox { VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Tag = day.Date,
                IsChecked = _selectedHolidays.Contains(day.Date) };
            selection.Checked += (_, _) => { _selectedHolidays.Add(day.Date); UpdateCalendarFooter(); };
            selection.Unchecked += (_, _) => { _selectedHolidays.Remove(day.Date); UpdateCalendarFooter(); };
            row.Children.Add(selection);
            var date = CalendarCell(day.Date.ToString("yyyy-MM-dd"));
            Grid.SetColumn(date, 1);
            row.Children.Add(date);
            var name = CalendarCell(day.Name);
            Grid.SetColumn(name, 2);
            row.Children.Add(name);
            var badge = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(day.IsOffDay ? "#E6F6EF" : "#FFF3E5")!,
                CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = day.IsOffDay ? "休息日" : "调休工作日", FontSize = 10,
                    Foreground = (Brush)new BrushConverter().ConvertFromString(day.IsOffDay ? "#21845D" : "#AA691F")!
                }
            };
            Grid.SetColumn(badge, 3);
            row.Children.Add(badge);
            var remove = new Button
            {
                Content = "×", Width = 28, Height = 28, ToolTip = "删除此日期",
                Style = (Style)Application.Current.Resources["ToolboxHeaderButton"],
                Foreground = (Brush)Application.Current.Resources["ToolboxMuted"]
            };
            remove.Click += (_, _) => DeleteHoliday([day.Date]);
            Grid.SetColumn(remove, 4);
            row.Children.Add(remove);
            var shell = new Border { BorderBrush = (Brush)Application.Current.Resources["ToolboxLine"],
                BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.White, Child = row };
            shell.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2) EditHoliday(day);
            };
            _holidayRows.Children.Add(shell);
        }
        UpdateCalendarFooter();
    }

    private void UpdateCalendarFooter()
    {
        _calendarCount.Text = $"共 {_calendarDays.Count} 条记录 · 已选 {_selectedHolidays.Count} 条";
        _deleteSelected.IsEnabled = _selectedHolidays.Count > 0;
        if (_selectAllHolidays is null) return;
        _updatingCalendarSelection = true;
        _selectAllHolidays.IsChecked = _calendarDays.Count > 0 && _selectedHolidays.Count == _calendarDays.Count
            ? true
            : _selectedHolidays.Count > 0 ? null : false;
        _updatingCalendarSelection = false;
    }

    public async Task SaveImmediateAsync(FrameworkElement source)
    {
        var old = _toto.ToolSettings;
        var next = old.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        if (!ReferenceEquals(source, _minutes)) return;
            if (!int.TryParse(_minutes.Text, out var minutes) || minutes < 0)
            {
                _minutes.Text = old.GetValueOrDefault("default_remind_minutes", "5");
                throw new InvalidOperationException("提前提醒分钟数必须为非负整数。");
        }
        next["default_remind_minutes"] = minutes.ToString();
        try { _toto.SaveToolSettings(next); }
        catch
        {
            _minutes.Text = old.GetValueOrDefault("default_remind_minutes", "5");
            throw;
        }
        await Task.CompletedTask;
    }

    private void EditLaunchKey()
    {
        var current = _application.Current.ToolKeys[nameof(ToolId.Toto)];
        _dialogs.EditShortcut("编辑快捷键 · Toto 工具列表单键", current, false, true, false,
            ValidateToolKey, async candidate =>
            {
                var old = _application.Current;
                var keys = old.ToolKeys.ToDictionary(item => item.Key, item => item.Value);
                keys[nameof(ToolId.Toto)] = candidate;
                var next = old with { ToolKeys = ToolLaunchKeys.Normalize(keys) };
                var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
                if (launcher is not null && !launcher.ApplySettings(next))
                {
                    launcher.ApplySettings(old);
                    throw new InvalidOperationException("单键与其他入口按键冲突，原设置保持不变。");
                }
                try
                {
                    await _application.SaveAsync(next);
                    _renderLaunchKey(candidate);
                }
                catch
                {
                    launcher?.ApplySettings(old);
                    await _application.SaveAsync(old);
                    throw;
                }
            }, ToolLaunchKeys.Toto.ToString());
    }

    private string? ValidateToolKey(string candidate)
    {
        try
        {
            var keys = _application.Current.ToolKeys.ToDictionary(item => item.Key, item => item.Value);
            keys[nameof(ToolId.Toto)] = candidate;
            ToolLaunchKeys.Normalize(keys);
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private void EditGlobalHotkey()
    {
        var current = _toto.Hotkey;
        _dialogs.EditShortcut("编辑快捷键 · Toto 全局直达", current, true, false, false,
            ValidateGlobalHotkey, async candidate =>
            {
                var values = _toto.ToolSettings.ToDictionary(item => item.Key, item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
                values["hotkey"] = candidate;
                _toto.SaveToolSettings(values);
                _renderGlobalKey(_toto.Hotkey);
            }, defaultValue: GlobalHotkeyPolicy.None);
    }

    private string? ValidateGlobalHotkey(string candidate)
    {
        try
        {
            var normalized = GlobalHotkeyPolicy.Normalize(candidate, optional: true);
            GlobalHotkeyPolicy.EnsureDistinct(_application.Current.LeaderShortcut,
                new[] { normalized, _jsonSettings.Current.GlobalShortcut }
                    .Concat(_application.Current.ToolGlobalShortcuts.Values).ToArray());
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private void EditWindowShortcut(string key, string label)
    {
        var current = _shortcuts.GetValueOrDefault(key, "");
        _dialogs.EditShortcut("编辑快捷键 · " + label, current, false, false, false,
            candidate => ValidateTotoLocalShortcut(key, candidate), async candidate =>
            {
                var values = _toto.ToolSettings.ToDictionary(item => item.Key, item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
                values[key] = candidate;
                _toto.SaveToolSettings(values);
                _shortcuts[key] = candidate;
                _renderShortcuts[key](candidate);
                await Task.CompletedTask;
            }, ShortcutDefaults[key], allowUnmodified: true);
    }

    private string? ValidateTotoLocalShortcut(string key, string candidate)
    {
        if (!WindowShortcut.TryNormalize(candidate, out var normalized)) return "快捷键格式无效。";
        var global = new[] { _application.Current.LeaderShortcut, _jsonSettings.Current.GlobalShortcut, _toto.Hotkey }
            .Concat(_application.Current.ToolGlobalShortcuts.Values)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != GlobalHotkeyPolicy.None);
        if (global.Any(value => value.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return "此按键已被应用或工具的全局热键占用。";
        if (_toto.ToolSettings.Any(item => item.Key.StartsWith("shortcut_", StringComparison.Ordinal) &&
            item.Key != key && WindowShortcut.TryNormalize(item.Value, out var existing) &&
            existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return "与另一个 Toto 窗口快捷键冲突。";
        return null;
    }

    private static readonly IReadOnlyDictionary<string, string> ShortcutDefaults = new Dictionary<string, string>
    {
        ["shortcut_add"] = "Alt+A", ["shortcut_history"] = "Alt+Q",
        ["shortcut_refresh"] = "Alt+R", ["shortcut_detail"] = "Alt+D", ["shortcut_edit"] = "Alt+E",
        ["shortcut_complete"] = "Alt+F", ["shortcut_cancel"] = "Alt+C"
    };

    private void EditDirectory() => _dialogs.EditFolder("选择 Toto 数据目录",
        _dataChanges.DisplayToolDirectory("toto"), async path =>
        {
            var old = _application.Current;
            _dataChanges.StageTool("toto", path);
            try
            {
                await _application.SaveAsync(old with
                {
                    PendingToolDataDirectories = new Dictionary<string, string>(_dataChanges.PendingTools,
                        StringComparer.OrdinalIgnoreCase)
                });
                _renderDirectory(_dataChanges.DisplayToolDirectory("toto"));
            }
            catch
            {
                _dataChanges.LoadPending(old.PendingDataRoot, old.PendingToolDataDirectories);
                throw;
            }
        });

    private FrameworkElement ReviewTimeControl(TotoTimePicker picker)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(picker);
        var clear = new Button
        {
            Content = "清空", Width = 66, Height = 34, Margin = new Thickness(9, 0, 0, 0),
            Style = (Style)Application.Current.Resources["TotoButton"]
        };
        clear.Click += (_, _) => { picker.Time = null; SaveDailyReview(); };
        controls.Children.Add(clear);
        return controls;
    }

    private void SaveDailyReview()
    {
        var old = _toto.ToolSettings;
        var values = old.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        values["daily_review_start_time"] = _dailyStart.Time ?? "";
        values["daily_review_end_time"] = _dailyEnd.Time ?? "";
        values["daily_review_workdays"] = _weekdaysOnly.IsChecked == true ? "1" : "0";
        try { _toto.SaveToolSettings(values); }
        catch (Exception ex)
        {
            _dailyStart.Time = old.GetValueOrDefault("daily_review_start_time");
            _dailyEnd.Time = old.GetValueOrDefault("daily_review_end_time");
            _weekdaysOnly.IsChecked = old.GetValueOrDefault("daily_review_workdays") == "1";
            System.Windows.MessageBox.Show(ex.Message, "Toolbox 设置", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditHoliday(TotoHoliday? existing)
    {
        var year = _year.SelectedItem is int selectedYear ? selectedYear : DateTime.Today.Year;
        var defaultDate = year == DateTime.Today.Year ? DateTime.Today : new DateTime(year, 1, 1);
        var date = new DatePicker
        {
            SelectedDate = existing?.Date.ToDateTime(TimeOnly.MinValue) ?? defaultDate,
            Width = 330, Height = 34, FontSize = 12,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        var name = new TextBox
        {
            Text = existing?.Name ?? string.Empty, Width = 330, Height = 34,
            Style = (Style)Application.Current.Resources["ToolboxFieldInput"],
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var type = new ComboBox { Width = 330, Height = 34,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        type.Items.Add("休息日");
        type.Items.Add("调休工作日");
        type.SelectedIndex = existing is null || existing.IsOffDay ? 0 : 1;
        var body = new StackPanel();
        AddHolidayField(body, "日期", date);
        AddHolidayField(body, "名称", name);
        AddHolidayField(body, "日期类型", type);
        _dialogs.EditGroup(existing is null ? "新增节假日" : "编辑节假日", "", body, () =>
            {
                if (date.SelectedDate is not { } selected || string.IsNullOrWhiteSpace(name.Text))
                    throw new InvalidOperationException("请选择日期并输入名称。");
                if (_year.SelectedItem is not int year || selected.Year != year)
                    throw new InvalidOperationException("日期必须属于当前选择的年份。");
                _toto.SaveHoliday(new TotoHoliday(name.Text.Trim(), DateOnly.FromDateTime(selected), type.SelectedIndex == 0));
                RefreshCalendar();
                return Task.CompletedTask;
            }, width: 380, saveText: existing is null ? "添加记录" : "保存");
    }

    private static void AddHolidayField(System.Windows.Controls.Panel panel, string label, FrameworkElement control)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, Foreground = (Brush)Application.Current.Resources["ToolboxMuted"],
            Margin = new Thickness(0, 0, 0, 6)
        });
        control.Margin = new Thickness(0, 0, 0, 14);
        panel.Children.Add(control);
    }

    private void RefreshCalendar()
    {
        if (_year.SelectedItem is not int year) return;
        try
        {
            _calendarDays.Clear();
            _calendarDays.AddRange(_toto.GetHolidays(year).OrderBy(day => day.Date));
            _selectedHolidays.Clear();
            RenderHolidayRows();
            _calendarStatus.Text = "所有更改已保存";
        }
        catch (Exception ex) { _calendarStatus.Text = ex.Message; }
    }

    private void DeleteHoliday(IReadOnlyCollection<DateOnly>? requested = null)
    {
        var dates = (requested ?? _selectedHolidays.ToArray()).Distinct().ToArray();
        if (dates.Length == 0) return;
        var prompt = dates.Length == 1 ? "删除该日期的节假日设置？" : $"删除选中的 {dates.Length} 条节假日设置？";
        if (System.Windows.MessageBox.Show(prompt, "Toto", MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes) return;
        try
        {
            foreach (var date in dates) _toto.DeleteHoliday(date);
            RefreshCalendar();
        }
        catch (Exception ex) { _calendarStatus.Text = ex.Message; }
    }

    private async Task DownloadCalendarAsync()
    {
        if (_year.SelectedItem is not int year) return;
        try { await _toto.DownloadHolidaysAsync(year); RefreshCalendar(); }
        catch (Exception ex) { _calendarStatus.Text = ex.Message; }
    }

}
