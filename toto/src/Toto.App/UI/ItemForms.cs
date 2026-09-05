using Toto.App.Data;
using Toto.App.Domain;
using Toto.App.Services;

namespace Toto.App.UI;

internal sealed class ItemEditForm : Form
{
    private readonly ItemRepository items;
    private readonly IReadOnlyDictionary<string, string> settings;
    private readonly TodoItem? editing;
    private readonly TextBox quick = new() { Dock = DockStyle.Top, PlaceholderText = "事项内容[@计划时间[@提前提醒分钟数]]" };
    private readonly TextBox content = new();
    private readonly DateTimePicker planned = TimePicker();
    private readonly DateTimePicker remind = TimePicker();
    private readonly TextBox note = new() { Multiline = true, Height = 90, Dock = DockStyle.Fill };

    public ItemEditForm(ItemRepository items, IReadOnlyDictionary<string, string> settings, TodoItem? editing)
    {
        this.items = items;
        this.settings = settings;
        this.editing = editing;
        Text = editing is null ? "toto - 新增事项" : "toto - 编辑事项";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(580, 370);
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

        var save = new Button { Text = "保存", DialogResult = DialogResult.None };
        save.Click += Save;
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
            { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([cancel, save]);
        layout.Controls.Add(buttons, 1, 5);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

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

        DialogResult = DialogResult.OK;
        Close();
    }

    private static DateTimePicker TimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", ShowCheckBox = true, Width = 220
    };

    private static void SetTime(DateTimePicker picker, DateTime? value)
    {
        picker.Checked = value is not null;
        if (value is not null) picker.Value = value.Value;
    }
}

internal sealed class ItemDetailForm : Form
{
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
                     ("事项内容", item.Content), ("计划时间", item.PlannedAt?.ToString(DateTimeText.Format) ?? ""),
                     ("提醒时间", item.RemindAt?.ToString(DateTimeText.Format) ?? ""),
                     (item.Status == ItemStatus.Active ? "提醒状态" : "结束状态",
                         item.Status == ItemStatus.Active ? item.ReminderStatus?.ToString() : item.Status.ToString()),
                     (item.Status == ItemStatus.Active ? "响铃时间" : "结束时间",
                         (item.Status == ItemStatus.Active ? item.RemindedAt : item.EndedAt)?.ToString(DateTimeText
                             .Format) ?? ""),
                     ("创建时间", item.CreatedAt.ToString(DateTimeText.Format)), ("备注", item.Note)
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

internal sealed class EndItemForm : Form
{
    private readonly TextBox note = new() { Multiline = true, Dock = DockStyle.Fill };
    public string Note => note.Text;

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
        var ok = new Button { Text = "确认", DialogResult = DialogResult.OK };
        layout.Controls.Add(ok, 0, 2);
        Controls.Add(layout);
        AcceptButton = ok;
    }
}