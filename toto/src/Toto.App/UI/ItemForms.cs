using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App.UI;

/// <summary>新增或编辑单个事项的独立非模态窗口，支持快速文本解析和完整字段编辑。</summary>
internal sealed class ItemEditForm : EscapeCloseForm
{
    /// <summary>事项成功保存后通知调用方刷新列表和调度器的非模态回调。</summary>
    public event Action? Saved;
    /// <summary>执行事项创建和更新的仓储。</summary>
    private readonly ItemRepository items;
    /// <summary>用于读取默认提醒分钟数的只读设置。</summary>
    private readonly IReadOnlyDictionary<string, string> settings;
    private readonly TodoItem? editing;
    private readonly TextBox quick = new() { Dock = DockStyle.Top, PlaceholderText = "事项内容[@计划时间[@提前提醒分钟数]]" };
    private readonly TextBox content = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker planned = TimePicker();
    private readonly DateTimePicker remind = TimePicker();
    private readonly TextBox note = new() { Multiline = true, Height = 90, Dock = DockStyle.Fill };

    /// <summary>初始化新增或编辑事项所需的控件和初始值。</summary>
    /// <param name="items">事项仓储。</param>
    /// <param name="settings">只读应用设置。</param>
    /// <param name="editing">待编辑事项；为空时创建新事项。</param>
    public ItemEditForm(ItemRepository items, IReadOnlyDictionary<string, string> settings, TodoItem? editing)
    {
        this.items = items;
        this.settings = settings;
        this.editing = editing;
        Text = editing is null ? "toto - 新增事项" : "toto - 编辑事项";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 370);
        var layout = new TableLayoutPanel
            { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (editing is null)
        {
            layout.Controls.Add(new Label { Text = "快速输入：", AutoSize = true }, 0, 0);
            layout.Controls.Add(quick, 1, 0);
            layout.Controls.Add(new Label { Text = "支持 HHmm、ddHHmm、MMddHHmm、yyyyMMddHHmm、+HHmm", AutoSize = true }, 1,
                1);
        }
        else
        {
            content.Text = editing.Content;
            SetTime(planned, editing.PlannedAt);
            SetTime(remind, editing.RemindAt);
            note.Text = editing.Note;
            layout.Controls.Add(new Label { Text = "事项内容：", AutoSize = true }, 0, 0);
            layout.Controls.Add(content, 1, 0);
            layout.Controls.Add(new Label { Text = "计划时间：", AutoSize = true }, 0, 1);
            layout.Controls.Add(planned, 1, 1);
            layout.Controls.Add(new Label { Text = "提醒时间：", AutoSize = true }, 0, 2);
            layout.Controls.Add(remind, 1, 2);
            layout.Controls.Add(new Label { Text = "备注：", AutoSize = true }, 0, 3);
            layout.Controls.Add(note, 1, 3);
        }

        var save = new Button { Text = "保存" };
        save.Click += Save;
        var cancel = new Button { Text = "取消" };
        cancel.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel
            { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft };
        // [cancel, save] 是 C# 12 collection expression，按目标参数类型创建控件数组。
        buttons.Controls.AddRange([cancel, save]);
        layout.Controls.Add(buttons, 1, 5);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    /// <summary>验证输入后创建新事项，或以修改后的记录副本更新现有事项。</summary>
    /// <param name="sender">触发 Click 事件的保存按钮。</param>
    /// <param name="e">WinForms Click 事件参数。</param>
    private void Save(object? sender, EventArgs e)
    {
        if (editing is null)
        {
            if (!QuickItemParser.TryParse(quick.Text,
                    int.TryParse(settings.GetValueOrDefault("default_remind_minutes"), out var minutes) ? minutes : 5,
                    out var value, out var plan, out var remindAt, out var error))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            items.Add(new TodoItem(Guid.NewGuid().ToString(), value, plan, remindAt, DateTime.Now,
                items.NextCreatedSeq(), ItemStatus.Active,
                remindAt is null ? ReminderStatus.None : ReminderStatus.Pending, null, null, ""));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(content.Text) || content.Text.Contains('\n') || content.Text.Contains('\r'))
            {
                MessageBox.Show("事项内容不能为空且不能包含换行。", Text);
                return;
            }

            DateTime? newRemind = remind.Checked ? remind.Value : null;
            var changed = newRemind != editing.RemindAt;
            var status = newRemind is null ? ReminderStatus.None :
                changed ? ReminderStatus.Pending : editing.ReminderStatus;
            var reminded = changed || newRemind is null ? null : editing.RemindedAt;
            // with 为 C# record 的非破坏性复制：保留未列出的值并生成新实例，不会修改原对象。
            if (!items.Update(editing with
                {
                    Content = content.Text.Trim(), PlannedAt = planned.Checked ? planned.Value : null,
                    RemindAt = newRemind, ReminderStatus = status, RemindedAt = reminded, Note = note.Text
                }))
            {
                MessageBox.Show("事项不存在或已被处理。", Text);
                return;
            }
        }

        // 非模态窗体不能依赖 DialogResult；通过事件将成功结果回传给打开它的窗口。
        Saved?.Invoke();
        Close();
    }

    /// <summary>创建支持勾选启用状态的日期时间选择控件。</summary>
    /// <returns>按应用格式配置的日期时间选择控件。</returns>
    private static DateTimePicker TimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = DateTimeText.DisplayFormat, ShowCheckBox = true, Width = 220
    };

    /// <summary>将可空时间值写入选择控件，并同步其勾选状态。</summary>
    /// <param name="picker">要更新的日期时间选择控件。</param>
    /// <param name="value">时间值；为空时取消勾选控件。</param>
    private static void SetTime(DateTimePicker picker, DateTime? value)
    {
        picker.Checked = value is not null;
        if (value is not null) picker.Value = value.Value;
    }
}

/// <summary>以只读字段展示单个进行中或历史事项详情的窗口。</summary>
internal sealed class ItemDetailForm : EscapeCloseForm
{
    /// <summary>初始化并显示指定事项的全部字段。</summary>
    /// <param name="item">要展示的事项。</param>
    public ItemDetailForm(TodoItem item)
    {
        Text = item.Status == ItemStatus.Active ? "toto - 进行中事项详情" : "toto - 历史事项详情";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 420);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var pair in new[]
                 {
                      ("事项内容", item.Content), ("计划时间", item.PlannedAt?.ToString(DateTimeText.DisplayFormat) ?? ""),
                      ("提醒时间", item.RemindAt?.ToString(DateTimeText.DisplayFormat) ?? ""),
                     (item.Status == ItemStatus.Active ? "提醒状态" : "结束状态",
                         item.Status == ItemStatus.Active ? item.ReminderStatus?.ToString() : item.Status.ToString()),
                     (item.Status == ItemStatus.Active ? "响铃时间" : "结束时间",
                          (item.Status == ItemStatus.Active ? item.RemindedAt : item.EndedAt)?.ToString(DateTimeText
                              .DisplayFormat) ?? ""),
                      ("创建时间", item.CreatedAt.ToString(DateTimeText.DisplayFormat)), ("备注", item.Note)
                 })
        {
            panel.Controls.Add(new Label { Text = pair.Item1 + "：", AutoSize = true }, 0, panel.RowCount);
            panel.Controls.Add(
                new TextBox
                {
                    Text = pair.Item2, ReadOnly = true, Multiline = pair.Item1 is "事项内容" or "备注", Dock = DockStyle.Fill
                }, 1, panel.RowCount);
            panel.RowCount++;
        }

        Controls.Add(panel);
    }
}

/// <summary>确认完成或取消事项，并允许编辑结束备注的独立非模态窗口。</summary>
internal sealed class EndItemForm : EscapeCloseForm
{
    private readonly TextBox note = new() { Multiline = true, Dock = DockStyle.Fill };
    /// <summary>获取用户输入的结束备注。</summary>
    public string Note => note.Text;
    /// <summary>用户确认结束操作时传出备注的非模态回调。</summary>
    public event Action<string>? Confirmed;

    /// <summary>初始化结束事项确认对话框。</summary>
    /// <param name="item">即将结束的事项。</param>
    /// <param name="status">要应用的完成或取消状态。</param>
    public EndItemForm(TodoItem item, ItemStatus status)
    {
        Text = "toto - " + (status == ItemStatus.Completed ? "完成" : "取消");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(500, 260);
        note.Text = item.Note;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3 };
        layout.Controls.Add(
            new Label
            {
                Text = $"确认“{(status == ItemStatus.Completed ? "已完成" : "已取消")}”：{item.Content}", AutoSize = true
            }, 0, 0);
        layout.Controls.Add(note, 0, 1);
        var ok = new Button { Text = "确认" };
        ok.Click += (_, _) =>
        {
            Confirmed?.Invoke(Note);
            Close();
        };
        layout.Controls.Add(ok, 0, 2);
        Controls.Add(layout);
        AcceptButton = ok;
    }
}
