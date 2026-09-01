using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.UI;

internal sealed class WorkCalendarForm : Form
{
    private readonly WorkCalendarRepository repository; private readonly NumericUpDown year = new() { Minimum = 2000, Maximum = 2100, Value = DateTime.Today.Year }; private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToAddRows = false, CellBorderStyle = DataGridViewCellBorderStyle.Single };
    public WorkCalendarForm(WorkCalendarRepository repository)
    {
        this.repository = repository; Text = "toto - 工作日管理"; StartPosition = FormStartPosition.CenterParent; Size = new Size(760, 500); var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) };
        MainForm.AddButton(top, "下载/更新", (_, _) => Download()); MainForm.AddButton(top, "刷新", (_, _) => LoadItems()); MainForm.AddButton(top, "新增", (_, _) => Edit(null)); MainForm.AddButton(top, "编辑", (_, _) => Edit(grid.CurrentRow?.DataBoundItem as HolidayCalendarDay)); MainForm.AddButton(top, "删除", (_, _) => { if (grid.CurrentRow?.DataBoundItem is HolidayCalendarDay item && MessageBox.Show("删除该日期的节假日设置？", Text, MessageBoxButtons.YesNo) == DialogResult.Yes) { repository.Delete(item.Date); LoadItems(); } }); top.Controls.Add(year); year.ValueChanged += (_, _) => LoadItems(); Controls.Add(grid); Controls.Add(top); LoadItems();
    }
    private void LoadItems() { try { grid.DataSource = repository.GetYear((int)year.Value); } catch (Exception exception) { MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void Download() { try { repository.Download((int)year.Value); LoadItems(); } catch (Exception exception) { MessageBox.Show($"下载节假日设置失败：{exception.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void Edit(HolidayCalendarDay? old) { using var form = new CalendarEditForm(old); if (form.ShowDialog(this) == DialogResult.OK) { repository.Upsert(form.Value); LoadItems(); } }
}

internal sealed class CalendarEditForm : Form
{
    private readonly DateTimePicker date = new() { Format = DateTimePickerFormat.Short }; private readonly TextBox name = new(); private readonly CheckBox isOffDay = new() { Text = "休息日", AutoSize = true }; public HolidayCalendarDay Value => new(name.Text.Trim(), DateOnly.FromDateTime(date.Value), isOffDay.Checked);
    public CalendarEditForm(HolidayCalendarDay? old)
    {
        Text = "节假日设置"; StartPosition = FormStartPosition.CenterParent; Size = new Size(380, 200); if (old is not null) { date.Value = old.Date.ToDateTime(TimeOnly.MinValue); name.Text = old.Name; isOffDay.Checked = old.IsOffDay; }
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 }; foreach (var pair in new (string, Control)[] { ("日期", date), ("名称", name), ("类型", isOffDay) }) { panel.Controls.Add(new Label { Text = pair.Item1 + "：", AutoSize = true }, 0, panel.RowCount); panel.Controls.Add(pair.Item2, 1, panel.RowCount++); } var save = new Button { Text = "保存", DialogResult = DialogResult.OK }; panel.Controls.Add(save, 1, panel.RowCount); Controls.Add(panel); AcceptButton = save;
    }
}
