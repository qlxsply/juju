using System.Media;
using System.Runtime.InteropServices;
using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.UI;

internal sealed class ReminderForm : Form
{
    private readonly ItemRepository repository;

    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToAddRows = false,
        CellBorderStyle = DataGridViewCellBorderStyle.Single
    };

    public ReminderForm(ItemRepository repository, IReadOnlyList<TodoItem> items, string title)
    {
        this.repository = repository;
        Text = title;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(720, 400);
        MainForm.AddColumn(grid, "事项内容", nameof(TodoItem.Content), 320, true);
        MainForm.AddColumn(grid, "计划时间", nameof(TodoItem.PlannedAt), 160);
        MainForm.AddColumn(grid, "提醒时间", nameof(TodoItem.RemindAt), 160);
        grid.DataSource = items;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) };
        MainForm.AddButton(bottom, "完成选中", (_, _) =>
        {
            if (grid.CurrentRow?.DataBoundItem is not TodoItem item) return;
            using var form = new EndItemForm(item, ItemStatus.Completed);
            if (form.ShowDialog(this) == DialogResult.OK)
                repository.End(item.Id, ItemStatus.Completed, form.Note, DateTime.Now);
        });
        MainForm.AddButton(bottom, "关闭", (_, _) => Close());
        Controls.Add(grid);
        Controls.Add(bottom);
        Shown += (_, _) =>
        {
            SystemSounds.Exclamation.Play();
            Flash();
        };
    }

    private void Flash()
    {
        var flash = new NativeMethods.Flashwinfo
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.Flashwinfo>(), hwnd = Handle, dwFlags = 3, uCount = 3,
            dwTimeout = 0
        };
        NativeMethods.FlashWindowEx(ref flash);
    }
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Flashwinfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    internal static extern bool FlashWindowEx(ref Flashwinfo info);
}