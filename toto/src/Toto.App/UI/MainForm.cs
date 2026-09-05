using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;
using Timer = System.Windows.Forms.Timer;

namespace Toto.App.UI;

/// <summary>显示进行中事项、处理应用内快捷键并提供新增、编辑和结束操作的主窗口。</summary>
internal sealed class MainForm : EscapeCloseForm
{
    /// <summary>进行中事项的数据访问入口。</summary>
    private readonly ItemRepository items;
    /// <summary>用于读取快捷键等运行时设置的仓储。</summary>
    private readonly SettingsRepository settings;
    private readonly ReminderScheduler scheduler;
    /// <summary>设置保存后重新注册全局快捷键的无参数委托。</summary>
    private readonly Action settingsChanged;
    private readonly DataGridView grid = Grid();
    private readonly BindingSource binding = new();
    private readonly Label status = new() { AutoSize = true };
    private readonly Timer urgencyTimer = new() { Interval = 60000 };

    /// <summary>初始化主窗口、数据绑定、WinForms 事件和刷新计时器。</summary>
    /// <param name="items">事项仓储。</param>
    /// <param name="settings">设置仓储。</param>
    /// <param name="scheduler">事项变更后要重新调度的服务。</param>
    /// <param name="settingsChanged">设置保存后的应用级回调。</param>
    public MainForm(ItemRepository items, SettingsRepository settings, ReminderScheduler scheduler,
        Action settingsChanged)
    {
        this.items = items;
        this.settings = settings;
        this.scheduler = scheduler;
        this.settingsChanged = settingsChanged;
        Text = "toto - 进行中事项";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(850, 450);
        Size = new Size(1100, 600);
        WindowStateTracker.RestoreAndTrack(this, settings, "main");
        KeyPreview = true;
        grid.DataSource = binding;
        // WinForms 事件以委托形式传递事件源和参数；这里用 lambda 忽略二者。
        grid.CellDoubleClick += (_, _) => ShowDetail();
        grid.CellFormatting += FormatCell;
        Controls.Add(grid);
        Controls.Add(status);
        status.Dock = DockStyle.Bottom;
        status.Padding = new Padding(10);
        urgencyTimer.Tick += (_, _) => grid.Invalidate();
        VisibleChanged += (_, _) => urgencyTimer.Enabled = Visible;
        FormClosing += HideOnClose;
        RefreshItems();
    }

    /// <summary>重新读取并绑定全部进行中事项。</summary>
    public void RefreshItems() => Bind(null);

    /// <summary>显示独立的新增事项窗口，并在保存后刷新调度。</summary>
    public void ShowEditor()
    {
        var editor = new ItemEditForm(items, settings, null);
        editor.Saved += OnItemSaved;
        editor.Show();
    }

    /// <summary>查询符合条件的进行中事项，并更新 WinForms 数据绑定源。</summary>
    /// <param name="criteria">可选查询条件；为空时不额外筛选。</param>
    // BindingSource 将集合变更适配给控件，与 Java Swing 手动实现 TableModel 的方式不同。
    private void Bind(QueryCriteria? criteria)
    {
        var result = items.GetActive(criteria);
        binding.DataSource = result;
        status.Text = $"进行中：{result.Count} 项";
    }

    /// <summary>编辑当前表格选中的事项。</summary>
    private void EditSelected()
    {
        if (Selected() is not { } item) return;
        var editor = new ItemEditForm(items, settings, item);
        editor.Saved += OnItemSaved;
        editor.Show();
    }

    /// <summary>显示当前选中事项的只读详情窗口。</summary>
    private void ShowDetail()
    {
        if (Selected() is { } item) new ItemDetailForm(item, settings).Show();
    }

    /// <summary>以指定结束状态关闭当前选中的事项。</summary>
    /// <param name="state">完成或取消状态。</param>
    private void EndSelected(ItemStatus state)
    {
        if (Selected() is not { } item) return;
        var form = new EndItemForm(item, state, settings) { TopMost = true };
        form.Confirmed += note =>
        {
            if (!items.End(item.Id, state, note, DateTime.Now)) return;
            OnItemSaved();
        };
        form.Show();
    }

    /// <summary>返回当前行绑定的事项；未选中或类型不匹配时返回空。</summary>
    private TodoItem? Selected() => grid.CurrentRow?.DataBoundItem as TodoItem;

    /// <summary>按计划时间和枚举值格式化表格单元格的颜色与文本。</summary>
    /// <param name="sender">发布格式化事件的表格。</param>
    /// <param name="e">包含单元格值和样式的 WinForms 事件参数。</param>
    private void FormatCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (grid.Rows[e.RowIndex].DataBoundItem is not TodoItem item || grid.Rows[e.RowIndex].Selected) return;
        if (item.PlannedAt is { } planned)
        {
            var color = planned <= DateTime.Now.AddHours(1) ? Color.Firebrick :
                planned.Date == DateTime.Today ? Color.ForestGreen :
                planned.Date == DateTime.Today.AddDays(1) ? Color.Olive : grid.ForeColor;
            e.CellStyle.ForeColor = color;
        }

        if (e.Value is ItemStatus state)
            e.Value = state == ItemStatus.Completed ? "已完成" : state == ItemStatus.Cancelled ? "已取消" : "进行中";
        if (e.Value is ReminderStatus reminder)
            e.Value = reminder switch { ReminderStatus.None => "无提醒", ReminderStatus.Pending => "未提醒", _ => "已提醒" };
    }

    /// <summary>拦截用户关闭主窗体的请求，改为隐藏窗口以保持托盘应用运行。</summary>
    /// <param name="sender">发布关闭事件的窗体。</param>
    /// <param name="e">可取消关闭操作的 WinForms 事件参数。</param>
    private void HideOnClose(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
    }

    /// <summary>优先处理配置的应用内快捷键，未匹配时交给基类处理。</summary>
    /// <param name="msg">按键对应的 Windows 消息，以引用传递。</param>
    /// <param name="keyData">修饰键和主键的组合。</param>
    /// <returns>已处理快捷键时为 <see langword="true"/>。</returns>
    // override 覆盖 Form 的消息钩子；ref 表示按引用传递，类似 Java 中可变包装对象但由语言直接支持。
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var configured = settings.Load();
        var actions = new Dictionary<string, Action>
        {
            ["shortcut_add"] = ShowEditor, ["shortcut_history"] = () => new HistoryForm(items, settings).Show(this),
            ["shortcut_settings"] = ShowSettings, ["shortcut_refresh"] = RefreshItems, ["shortcut_detail"] = ShowDetail,
            ["shortcut_edit"] = EditSelected, ["shortcut_complete"] = () => EndSelected(ItemStatus.Completed),
            ["shortcut_cancel"] = () => EndSelected(ItemStatus.Cancelled)
        };
        foreach (var (name, action) in actions)
            if (Hotkey.TryParse(configured.GetValueOrDefault(name), out var key) && key == keyData)
            {
                action();
                return true;
            }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>显示设置对话框，并在保存后调用应用级设置变更委托。</summary>
    private void ShowSettings()
    {
        var form = new SettingsForm(settings, scheduler);
        form.SettingsSaved += settingsChanged;
        form.Show();
    }

    /// <summary>统一处理新增、编辑或结束操作成功后的刷新和重新调度。</summary>
    private void OnItemSaved()
    {
        RefreshItems();
        scheduler.Reschedule();
    }

    /// <summary>创建用于事项列表的标准只读表格及其基础列。</summary>
    /// <returns>配置完成但尚未绑定数据源的表格。</returns>
    internal static DataGridView Grid()
    {
        var value = new DataGridView
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, AllowUserToOrderColumns = false,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            // 自动提示会显示 DateTime 原始值，并且不使用列的显示格式。
            ShowCellToolTips = false
        };
        AddColumn(value, "事项内容", nameof(TodoItem.Content), 260);
        AddColumn(value, "计划时间", nameof(TodoItem.PlannedAt), 155);
        AddColumn(value, "提醒时间", nameof(TodoItem.RemindAt), 155);
        AddColumn(value, "提醒状态", nameof(TodoItem.ReminderStatus), 90);
        AddColumn(value, "创建时间", nameof(TodoItem.CreatedAt), 155);
        AddColumn(value, "备注", nameof(TodoItem.Note), 240, true);
        return value;
    }

    /// <summary>向表格添加绑定到模型属性的文本列。</summary>
    /// <param name="grid">要添加列的表格。</param>
    /// <param name="header">列标题。</param>
    /// <param name="property">绑定对象的属性名。</param>
    /// <param name="width">非填充列的像素宽度。</param>
    /// <param name="fill">是否填充剩余可用宽度。</param>
    internal static void AddColumn(DataGridView grid, string header, string property, int width, bool fill = false) =>
        grid.Columns.Add(CreateColumn(header, property, width, fill));

    /// <summary>创建数据绑定列，并将所有 <c>*At</c> 日期时间属性显示为不含秒的格式。</summary>
    private static DataGridViewTextBoxColumn CreateColumn(string header, string property, int width, bool fill)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header, DataPropertyName = property, Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        if (property.EndsWith("At", StringComparison.Ordinal)) column.DefaultCellStyle.Format = DateTimeText.DisplayFormat;
        return column;
    }

    /// <summary>创建按钮、订阅其 Click 事件并添加到父控件。</summary>
    /// <param name="parent">承载按钮的控件。</param>
    /// <param name="text">按钮显示文本。</param>
    /// <param name="handler">符合 <see cref="EventHandler"/> 委托签名的点击处理程序。</param>
    // EventHandler 是 C# 委托类型；订阅后可由多个处理程序共同响应同一个事件。
    internal static void AddButton(Control parent, string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28 };
        button.Click += handler;
        parent.Controls.Add(button);
    }
}

/// <summary>将用户配置的快捷键文本解析为 WinForms <see cref="Keys"/> 标志组合。</summary>
internal static class Hotkey
{
    /// <summary>尝试把加号分隔的修饰键和主键文本解析为键组合。</summary>
    /// <param name="text">例如 <c>Ctrl+Alt+Space</c> 的配置文本。</param>
    /// <param name="keys">解析成功时得到的键组合。</param>
    /// <returns>文本有效且至少包含一个键时为 <see langword="true"/>。</returns>
    public static bool TryParse(string? text, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) keys |= Keys.Shift;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) keys |= Keys.LWin;
            else if (!Enum.TryParse<Keys>(part, true, out var key)) return false;
            else keys |= key;
        }

        return keys != Keys.None;
    }
}
