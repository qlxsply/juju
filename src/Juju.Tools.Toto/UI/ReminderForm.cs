using System.Media;
using System.Runtime.InteropServices;
using Toto.App.Data;
using Toto.App.Domain;

namespace Toto.App.UI;

/// <summary>置顶显示到期事项或工作日计划事项，并可直接完成选中事项的提醒窗口。</summary>
internal sealed class ReminderForm : EscapeCloseForm
{
    /// <summary>执行事项完成操作的仓储。</summary>
    private readonly ItemRepository repository;
    private readonly SettingsRepository settings;
    private readonly List<TodoItem> displayedItems;

    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToAddRows = false,
        CellBorderStyle = DataGridViewCellBorderStyle.Single,
        ShowCellToolTips = false
    };

    /// <summary>初始化提醒窗口并绑定要显示的事项。</summary>
    /// <param name="repository">事项仓储。</param>
    /// <param name="items">显示在表格中的只读事项集合。</param>
    /// <param name="settings">应用设置和窗口状态仓储。</param>
    /// <param name="title">窗口标题。</param>
    public ReminderForm(ItemRepository repository, IReadOnlyList<TodoItem> items, SettingsRepository settings, string title)
    {
        this.repository = repository;
        this.settings = settings;
        displayedItems = [..items];
        Text = title;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(720, 400);
        WindowStateTracker.RestoreAndTrack(this, settings, "reminder");
        MainForm.AddColumn(grid, "事项内容", nameof(TodoItem.Content), 320, true);
        MainForm.AddColumn(grid, "计划时间", nameof(TodoItem.PlannedAt), 160);
        MainForm.AddColumn(grid, "提醒时间", nameof(TodoItem.RemindAt), 160);
        grid.DataSource = displayedItems;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) };
        MainForm.AddButton(bottom, "完成选中", (_, _) =>
        {
            if (grid.CurrentRow?.DataBoundItem is not TodoItem item) return;
            var form = new EndItemForm(item, ItemStatus.Completed, settings) { TopMost = true };
            form.Confirmed += note =>
            {
                if (!repository.End(item.Id, ItemStatus.Completed, note, DateTime.Now)) return;
                displayedItems.RemoveAll(value => value.Id == item.Id);
                grid.DataSource = null;
                grid.DataSource = displayedItems;
            };
            form.Show();
        });
        MainForm.AddButton(bottom, "关闭", (_, _) => Close());
        Controls.Add(grid);
        Controls.Add(bottom);
        // Shown 是窗体生命周期事件；事件处理程序在原生窗口首次显示后运行。
        Shown += (_, _) =>
        {
            SystemSounds.Exclamation.Play();
            Flash();
        };
    }

    /// <summary>调用 Windows API，使提醒窗口在任务栏中闪烁。</summary>
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

/// <summary>声明本窗口使用的 user32.dll 非托管互操作成员。</summary>
internal static class NativeMethods
{
    /// <summary>定义传递给 <see cref="FlashWindowEx"/> 的 Win32 结构体。</summary>
    // StructLayout 固定字段内存布局，确保托管结构体与 C/C++ Win32 ABI 一致。
    [StructLayout(LayoutKind.Sequential)]
    internal struct Flashwinfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    /// <summary>请求 Windows 将指定窗口闪烁为需要用户注意。</summary>
    /// <param name="info">包含窗口句柄和闪烁选项的 Win32 结构体。</param>
    /// <returns>调用前窗口是否处于活动状态。</returns>
    // P/Invoke 将 C# extern 方法映射到 DLL 导出函数，Java 通常需通过 JNI/JNA 实现此类调用。
    [DllImport("user32.dll")]
    internal static extern bool FlashWindowEx(ref Flashwinfo info);
}
