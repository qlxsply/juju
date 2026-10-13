using Toto.App.Data;
using Toto.App.Domain;
using Timer = System.Windows.Forms.Timer;

namespace Toto.App.UI;

/// <summary>按内容和备注筛选已结束事项，并以分页表格显示历史记录的窗口。</summary>
internal sealed class HistoryForm : EscapeCloseForm
{
    /// <summary>查询历史事项的仓储。</summary>
    private readonly ItemRepository items;
    private readonly SettingsRepository settings;
    /// <summary>将当前页结果绑定到表格的 WinForms 绑定源。</summary>
    private readonly DataGridView grid = MainForm.Grid();
    private readonly BindingSource binding = new();
    private readonly Label pageLabel = new() { AutoSize = true };
    private readonly TextBox content = new() { PlaceholderText = "事项内容包含", Width = 180, AutoSize = false, Height = 28, Margin = new Padding(3) };
    private readonly TextBox note = new() { PlaceholderText = "备注包含", Width = 150, AutoSize = false, Height = 28, Margin = new Padding(3) };
    private readonly ComboBox endedPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 96, Height = 28, Margin = new Padding(3) };
    private readonly DateTimePicker endedFrom = DatePicker();
    private readonly DateTimePicker endedTo = DatePicker();
    private readonly ComboBox pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
    /// <summary>合并连续输入事件，避免每个按键都立即读取历史数据。</summary>
    private readonly Timer searchTimer = new() { Interval = 200 };
    private QueryCriteria criteria = new();
    private int page = 1;
    private int total;

    /// <summary>初始化历史查询控件、分页操作和数据绑定。</summary>
    /// <param name="items">历史事项数据的仓储。</param>
    public HistoryForm(ItemRepository items, SettingsRepository settings)
    {
        this.items = items;
        this.settings = settings;
        Text = "toto - 历史事项";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1150, 620);
        WindowStateTracker.RestoreAndTrack(this, settings, "history");
        grid.Columns.RemoveAt(3);
        MainForm.AddColumn(grid, "结束状态", nameof(TodoItem.Status), 90);
        MainForm.AddColumn(grid, "结束时间", nameof(TodoItem.EndedAt), 155);
        grid.CellDoubleClick += (_, _) =>
        {
            if (grid.CurrentRow?.DataBoundItem is TodoItem item) new ItemDetailForm(item, settings).Show();
        };
        grid.DataSource = binding;
        searchTimer.Tick += (_, _) => SearchNow();
        FormClosed += (_, _) => searchTimer.Dispose();
        // collection expression 根据 AddRange 参数类型推断并创建整数数组。
        pageSize.Items.AddRange([100, 200, 500]);
        pageSize.SelectedItem = 200;
        endedPreset.Items.AddRange(["不限", "本周", "上一周", "本月", "上一月", "本年", "上一年", "自定义"]);
        endedPreset.SelectedIndex = 0;
        endedPreset.SelectedIndexChanged += (_, _) => ApplyEndedPreset();
        content.TextChanged += (_, _) => ScheduleSearch();
        note.TextChanged += (_, _) => ScheduleSearch();
        endedFrom.ValueChanged += (_, _) => ScheduleSearch();
        endedTo.ValueChanged += (_, _) => ScheduleSearch();
        // 勾选或取消日期控件内置复选框不会保证触发 ValueChanged，因此也监听鼠标和键盘操作。
        endedFrom.MouseUp += (_, _) => ScheduleSearch();
        endedTo.MouseUp += (_, _) => ScheduleSearch();
        endedFrom.KeyUp += (_, _) => ScheduleSearch();
        endedTo.KeyUp += (_, _) => ScheduleSearch();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), WrapContents = false };
        MainForm.AddButton(top, "查询", (_, _) => SearchNow());
        MainForm.AddButton(top, "重置", (_, _) =>
        {
            content.Clear();
            note.Clear();
            endedPreset.SelectedIndex = 0;
            endedFrom.Checked = false;
            endedTo.Checked = false;
            SearchNow();
        });
        top.Controls.AddRange([content, note, new Label { Text = "结束日期：", AutoSize = true, Padding = new Padding(5, 6, 0, 0) }, endedPreset, endedFrom, endedTo]);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) };
        // 标签使用与按钮相同的 28px 高度，避免 FlowLayoutPanel 中出现垂直错位。
        pageLabel.AutoSize = false;
        pageLabel.Size = new Size(140, 28);
        pageLabel.TextAlign = ContentAlignment.MiddleCenter;
        pageLabel.Margin = new Padding(3);
        MainForm.AddButton(bottom, "首页", (_, _) =>
        {
            page = 1;
            LoadPage();
        });
        MainForm.AddButton(bottom, "上一页", (_, _) =>
        {
            page = Math.Max(1, page - 1);
            LoadPage();
        });
        MainForm.AddButton(bottom, "下一页", (_, _) =>
        {
            page = Math.Min(MaxPage(), page + 1);
            LoadPage();
        });
        MainForm.AddButton(bottom, "末页", (_, _) =>
        {
            page = MaxPage();
            LoadPage();
        });
        bottom.Controls.Add(pageLabel);
        bottom.Controls.Add(new Label
        {
            Text = "每页：", AutoSize = false, Size = new Size(58, 28), TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(3, 3, 0, 3)
        });
        bottom.Controls.Add(pageSize);
        pageSize.SelectedIndexChanged += (_, _) =>
        {
            page = 1;
            LoadPage();
        };
        Controls.Add(grid);
        Controls.Add(top);
        Controls.Add(bottom);
        LoadPage();
    }

    /// <summary>按当前筛选条件和页码读取历史事项，并更新分页提示。</summary>
    private void LoadPage()
    {
        var result = items.GetHistory(criteria, page, (int)pageSize.SelectedItem!);
        total = result.TotalCount;
        binding.DataSource = result.Items;
        pageLabel.Text = $"共 {total} 条，第 {page}/{MaxPage()} 页";
    }

    /// <summary>在筛选条件变化后延迟查询，将连续输入合并为一次数据读取。</summary>
    private void ScheduleSearch()
    {
        searchTimer.Stop();
        searchTimer.Start();
    }

    /// <summary>立即根据所有当前筛选控件重建条件，并从第一页显示结果。</summary>
    private void SearchNow()
    {
        searchTimer.Stop();
        criteria = new QueryCriteria
        {
            Content = content.Text, Note = note.Text,
            EndedFrom = endedFrom.Checked ? endedFrom.Value.Date : null,
            EndedTo = endedTo.Checked ? endedTo.Value.Date.AddDays(1) : null
        };
        page = 1;
        LoadPage();
    }

    /// <summary>根据总记录数和选定页大小计算至少为一的最大页码。</summary>
    /// <returns>可导航到的最后页码。</returns>
    private int MaxPage() => Math.Max(1, (int)Math.Ceiling(total / (double)(int)pageSize.SelectedItem!));

    /// <summary>创建精确到日期的可选范围控件；查询时结束日期会转换为下一日的排他边界。</summary>
    private static DateTimePicker DatePicker() => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", ShowCheckBox = true, Width = 112, Height = 28,
        Margin = new Padding(3)
    };

    /// <summary>将结束日期预设转换为包含起始日、排除次日的日期范围。</summary>
    private void ApplyEndedPreset()
    {
        var today = DateTime.Today;
        DateTime? from = null;
        DateTime? toInclusive = null;
        switch (endedPreset.SelectedItem as string)
        {
            case "本周":
                from = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
                toInclusive = from.Value.AddDays(6);
                break;
            case "上一周":
                toInclusive = today.AddDays(-((int)today.DayOfWeek + 6) % 7).AddDays(-1);
                from = toInclusive.Value.AddDays(-6);
                break;
            case "本月":
                from = new DateTime(today.Year, today.Month, 1);
                toInclusive = from.Value.AddMonths(1).AddDays(-1);
                break;
            case "上一月":
                from = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
                toInclusive = from.Value.AddMonths(1).AddDays(-1);
                break;
            case "本年":
                from = new DateTime(today.Year, 1, 1);
                toInclusive = new DateTime(today.Year, 12, 31);
                break;
            case "上一年":
                from = new DateTime(today.Year - 1, 1, 1);
                toInclusive = new DateTime(today.Year - 1, 12, 31);
                break;
        }
        endedFrom.Checked = from is not null;
        endedTo.Checked = toInclusive is not null;
        if (from is not null) endedFrom.Value = from.Value;
        if (toInclusive is not null) endedTo.Value = toInclusive.Value;
        ScheduleSearch();
    }
}
