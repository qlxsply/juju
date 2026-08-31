using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.UI;

internal sealed class HistoryForm : Form
{
    private readonly ItemRepository items; private readonly DataGridView grid = MainForm.Grid(); private readonly BindingSource binding = new(); private readonly Label pageLabel = new() { AutoSize = true }; private readonly TextBox content = new() { PlaceholderText = "事项内容包含", Width = 180 }; private readonly TextBox note = new() { PlaceholderText = "备注包含", Width = 150 }; private readonly ComboBox pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 }; private QueryCriteria criteria = new(); private int page = 1; private int total;
    public HistoryForm(ItemRepository items)
    {
        this.items = items; Text = "toto - 历史事项"; StartPosition = FormStartPosition.CenterParent; Size = new Size(1150, 620); grid.Columns.RemoveAt(3); MainForm.AddColumn(grid, "结束状态", nameof(TodoItem.Status), 90); MainForm.AddColumn(grid, "结束时间", nameof(TodoItem.EndedAt), 155); grid.CellDoubleClick += (_, _) => { if (grid.CurrentRow?.DataBoundItem is TodoItem item) new ItemDetailForm(item).ShowDialog(this); }; grid.DataSource = binding;
        pageSize.Items.AddRange([100, 200, 500]); pageSize.SelectedItem = 200; var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8) }; MainForm.AddButton(top, "查询", (_, _) => { criteria = new QueryCriteria { Content = content.Text, Note = note.Text }; page = 1; LoadPage(); }); MainForm.AddButton(top, "重置", (_, _) => { content.Clear(); note.Clear(); criteria = new(); page = 1; LoadPage(); }); top.Controls.AddRange([content, note]); var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) }; MainForm.AddButton(bottom, "首页", (_, _) => { page = 1; LoadPage(); }); MainForm.AddButton(bottom, "上一页", (_, _) => { page = Math.Max(1, page - 1); LoadPage(); }); MainForm.AddButton(bottom, "下一页", (_, _) => { page = Math.Min(MaxPage(), page + 1); LoadPage(); }); MainForm.AddButton(bottom, "末页", (_, _) => { page = MaxPage(); LoadPage(); }); bottom.Controls.Add(pageLabel); bottom.Controls.Add(new Label { Text = "每页：", AutoSize = true, Padding = new Padding(16, 5, 0, 0) }); bottom.Controls.Add(pageSize); pageSize.SelectedIndexChanged += (_, _) => { page = 1; LoadPage(); }; Controls.Add(grid); Controls.Add(top); Controls.Add(bottom); LoadPage();
    }
    private void LoadPage() { var result = items.GetHistory(criteria, page, (int)pageSize.SelectedItem!); total = result.TotalCount; binding.DataSource = result.Items; pageLabel.Text = $"共 {total} 条，第 {page}/{MaxPage()} 页"; }
    private int MaxPage() => Math.Max(1, (int)Math.Ceiling(total / (double)(int)pageSize.SelectedItem!));
}
