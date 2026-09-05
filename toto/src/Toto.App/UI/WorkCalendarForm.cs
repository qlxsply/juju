using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.UI;

/// <summary>提供节假日和调休日期的查看、下载、添加、编辑与删除界面。</summary>
internal sealed class WorkCalendarForm : EscapeCloseForm
{
    /// <summary>保存日历数据的仓储。</summary>
    private readonly WorkCalendarRepository repository;
    /// <summary>选择当前显示年份的数值控件。</summary>
    private readonly NumericUpDown year = new()
    {
        Minimum = 2020, Maximum = LatestSelectableYear(), Value = DateTime.Today.Year, Width = 86, Height = 28,
        TextAlign = HorizontalAlignment.Center, Margin = new Padding(3)
    };

    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = true, ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToAddRows = false,
        CellBorderStyle = DataGridViewCellBorderStyle.Single,
        ShowCellToolTips = false
    };

    /// <summary>初始化工作日管理窗口。</summary>
    /// <param name="repository">要操作的日历仓储。</param>
    public WorkCalendarForm(WorkCalendarRepository repository)
    {
        this.repository = repository;
        Text = "toto - 工作日管理";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 500);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), WrapContents = false };
        MainForm.AddButton(top, "下载/更新", (_, _) => Download());
        MainForm.AddButton(top, "刷新", (_, _) => LoadItems());
        MainForm.AddButton(top, "新增", (_, _) => Edit(null));
        MainForm.AddButton(top, "编辑", (_, _) => Edit(grid.CurrentRow?.DataBoundItem as HolidayCalendarDay));
        MainForm.AddButton(top, "删除", (_, _) =>
        {
            if (grid.CurrentRow?.DataBoundItem is HolidayCalendarDay item &&
                MessageBox.Show("删除该日期的节假日设置？", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                repository.Delete(item.Date);
                LoadItems();
            }
        });
        top.Controls.Add(year);
        // WinForms 控件事件使用委托回调；lambda 是内联事件处理程序。
        year.ValueChanged += (_, _) => LoadItems();
        Controls.Add(grid);
        Controls.Add(top);
        LoadItems();
    }

    /// <summary>计算当前客户端日期允许选择和下载的最大年份。</summary>
    /// <remarks>第 N 年只在第 N-1 年 12 月 1 日后可选，避免上游尚未发布时产生无意义下载错误。</remarks>
    private static int LatestSelectableYear()
    {
        var today = DateTime.Today;
        return today >= new DateTime(today.Year, 12, 1) ? today.Year + 1 : today.Year;
    }

    /// <summary>读取所选年份的日历数据并绑定到表格。</summary>
    private void LoadItems()
    {
        try
        {
            grid.DataSource = repository.GetYear((int)year.Value);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>下载所选年份的日历数据，然后刷新表格。</summary>
    private void Download()
    {
        if ((int)year.Value > LatestSelectableYear())
        {
            MessageBox.Show("该年份的法定节假日尚未到允许下载的日期。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            repository.Download((int)year.Value);
            LoadItems();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"下载节假日设置失败：{exception.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>打开新增或编辑日历日期的对话框。</summary>
    /// <param name="old">待编辑的旧值；为空时新增日期。</param>
    private void Edit(HolidayCalendarDay? old)
    {
        var form = new CalendarEditForm(old);
        form.Saved += item =>
        {
            repository.Upsert(item);
            LoadItems();
        };
        form.Show();
    }
}

/// <summary>编辑单个节假日或调休日期的独立非模态窗口。</summary>
internal sealed class CalendarEditForm : EscapeCloseForm
{
    /// <summary>日历日期保存后传出新值的非模态回调。</summary>
    public event Action<HolidayCalendarDay>? Saved;
    private readonly DateTimePicker date = new() { Format = DateTimePickerFormat.Short };
    private readonly TextBox name = new();
    private readonly CheckBox isOffDay = new() { Text = "休息日", AutoSize = true };
    /// <summary>根据当前控件内容构造待保存的日历日期。</summary>
    /// <remarks><c>HolidayCalendarDay</c> 是不可变记录类型，<c>new</c> 在此创建其新实例。</remarks>
    public HolidayCalendarDay Value => new(name.Text.Trim(), DateOnly.FromDateTime(date.Value), isOffDay.Checked);

    /// <summary>初始化日历日期编辑对话框。</summary>
    /// <param name="old">要预填的旧值；为空时创建空白对话框。</param>
    public CalendarEditForm(HolidayCalendarDay? old)
    {
        Text = "节假日设置";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(380, 200);
        // C# 的 is not null 是可空流分析友好的模式匹配，而不是 Java 的普通引用比较。
        if (old is not null)
        {
            date.Value = old.Date.ToDateTime(TimeOnly.MinValue);
            name.Text = old.Name;
            isOffDay.Checked = old.IsOffDay;
        }

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
        foreach (var pair in new (string, Control)[] { ("日期", date), ("名称", name), ("类型", isOffDay) })
        {
            panel.Controls.Add(new Label { Text = pair.Item1 + "：", AutoSize = true }, 0, panel.RowCount);
            panel.Controls.Add(pair.Item2, 1, panel.RowCount++);
        }

        var save = new Button { Text = "保存" };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                MessageBox.Show("名称不能为空。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Saved?.Invoke(Value);
            Close();
        };
        panel.Controls.Add(save, 1, panel.RowCount);
        Controls.Add(panel);
        AcceptButton = save;
    }
}
