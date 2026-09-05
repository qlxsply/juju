using Toto.App.Data;
using Toto.App.Services;

namespace Toto.App.UI;

/// <summary>编辑全局热键、提醒时间、应用内快捷键和开机启动选项的独立非模态窗口。</summary>
internal sealed class SettingsForm : EscapeCloseForm
{
    /// <summary>设置成功保存后通知应用重新注册快捷键的非模态回调。</summary>
    public event Action? SettingsSaved;
    /// <summary>读取和保存配置的仓储。</summary>
    private readonly SettingsRepository repository;
    /// <summary>设置变更后需重新安排的提醒调度器。</summary>
    private readonly ReminderScheduler scheduler;
    private readonly Dictionary<string, TextBox> values = new();
    private readonly CheckBox autoStart = new() { Text = "登录 Windows 后自动启动 toto", AutoSize = true };
    private readonly CheckBox workStartEnabled = new() { Text = "上班时间自动弹出全部进行中事项", AutoSize = true };
    private readonly CheckBox workEndEnabled = new() { Text = "下班时间自动弹出全部进行中事项", AutoSize = true };

    /// <summary>初始化并以现有设置填充窗口。</summary>
    /// <param name="repository">设置仓储。</param>
    /// <param name="scheduler">保存成功后要重新调度的提醒服务。</param>
    public SettingsForm(SettingsRepository repository, ReminderScheduler scheduler)
    {
        this.repository = repository;
        this.scheduler = scheduler;
        Text = "toto - 设置";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(570, 510);
        var settings = repository.Load();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var general = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        Add(general, "全局唤醒快捷键", "hotkey", settings);
        Add(general, "默认提前提醒分钟", "default_remind_minutes", settings);
        autoStart.Checked = settings.GetValueOrDefault("auto_start") == "1";
        general.Controls.Add(autoStart, 1, 2);
        tabs.TabPages.Add(new TabPage("常规") { Controls = { general } });
        var shortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        foreach (var (label, key) in new[]
                 {
                     ("新增", "shortcut_add"), ("历史", "shortcut_history"), ("设置", "shortcut_settings"),
                     ("刷新", "shortcut_refresh"), ("详情", "shortcut_detail"), ("编辑", "shortcut_edit"),
                     ("完成", "shortcut_complete"), ("取消", "shortcut_cancel")
                 }) Add(shortcuts, label, key, settings);
        tabs.TabPages.Add(new TabPage("快捷键") { Controls = { shortcuts } });
        var work = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        workStartEnabled.Checked = settings.GetValueOrDefault("work_start_popup_enabled") == "1";
        workEndEnabled.Checked = settings.GetValueOrDefault("work_end_popup_enabled") == "1";
        work.Controls.Add(workStartEnabled, 1, 0);
        work.RowCount = 1;
        Add(work, "上班时间 HH:mm", "work_start_time", settings);
        work.Controls.Add(workEndEnabled, 1, 2);
        work.RowCount = 3;
        Add(work, "下班时间 HH:mm", "work_end_time", settings);
        // CheckedChanged 是 WinForms 事件，+= 将 lambda 委托订阅到控件的状态变更通知。
        workStartEnabled.CheckedChanged += (_, _) => values["work_start_time"].Enabled = workStartEnabled.Checked;
        workEndEnabled.CheckedChanged += (_, _) => values["work_end_time"].Enabled = workEndEnabled.Checked;
        values["work_start_time"].Enabled = workStartEnabled.Checked;
        values["work_end_time"].Enabled = workEndEnabled.Checked;
        tabs.TabPages.Add(new TabPage("工作日提醒") { Controls = { work } });
        var save = new Button { Text = "保存", Dock = DockStyle.Bottom, Height = 36 };
        save.Click += Save;
        Controls.Add(tabs);
        Controls.Add(save);
    }

    /// <summary>向表格布局添加标签和与指定配置键关联的文本框。</summary>
    /// <param name="layout">承载设置行的布局面板。</param>
    /// <param name="label">显示给用户的标签。</param>
    /// <param name="key">设置字典中的键。</param>
    /// <param name="settings">用于预填文本框的只读设置。</param>
    private void Add(TableLayoutPanel layout, string label, string key, IReadOnlyDictionary<string, string> settings)
    {
        var row = layout.RowCount++;
        layout.Controls.Add(new Label { Text = label + "：", AutoSize = true }, 0, row);
        var box = new TextBox { Text = settings.GetValueOrDefault(key), Width = 250 };
        values[key] = box;
        layout.Controls.Add(box, 1, row);
    }

    /// <summary>验证输入、保存设置、更新开机启动状态并重新安排提醒。</summary>
    /// <param name="sender">触发 Click 事件的保存按钮。</param>
    /// <param name="e">WinForms Click 事件参数。</param>
    private void Save(object? sender, EventArgs e)
    {
        if (!int.TryParse(values["default_remind_minutes"].Text, out var minutes) || minutes < 0)
        {
            MessageBox.Show("默认提前提醒分钟数必须为非负整数。", Text);
            return;
        }

        if (!TimeOnly.TryParse(values["work_start_time"].Text, out _) ||
            !TimeOnly.TryParse(values["work_end_time"].Text, out _))
        {
            MessageBox.Show("上班和下班时间必须为 HH:mm。", Text);
            return;
        }

        var shortcuts = values.Where(x => x.Key.StartsWith("shortcut_")).Select(x => x.Value.Text).ToArray();
        if (shortcuts.Any(string.IsNullOrWhiteSpace) ||
            shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length ||
            shortcuts.Any(x => !Hotkey.TryParse(x, out _)))
        {
            MessageBox.Show("八个应用内快捷键必须有效且不能重复。", Text);
            return;
        }

        var all = repository.Load().ToDictionary(x => x.Key, x => x.Value);
        foreach (var pair in values) all[pair.Key] = pair.Value.Text.Trim();
        all["auto_start"] = autoStart.Checked ? "1" : "0";
        all["work_start_popup_enabled"] = workStartEnabled.Checked ? "1" : "0";
        all["work_end_popup_enabled"] = workEndEnabled.Checked ? "1" : "0";
        if (!new StartupService().SetEnabled(autoStart.Checked))
        {
            MessageBox.Show("更新开机启动失败，设置未保存。", Text);
            return;
        }

        repository.Save(all);
        scheduler.Reschedule();
        SettingsSaved?.Invoke();
        Close();
    }
}
