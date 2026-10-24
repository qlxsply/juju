using System.Collections.ObjectModel;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Juju.Platform.Windows;
using Toto.App;
using Toto.App.Domain;

namespace Juju.App.Views;

public partial class TotoReminderWindow : Window
{
    private readonly TotoToolHost _host;
    private readonly ObservableCollection<TotoItemRow> _reminderItems;
    private bool _notified;
    public event Action? ItemChanged;

    internal TotoReminderWindow(TotoToolHost host, IReadOnlyList<TodoItem> items, string title)
    {
        _host = host;
        Title = title;
        _reminderItems = new ObservableCollection<TotoItemRow>(items.Select(item => new TotoItemRow(item)));
        InitializeComponent();
        DataContext = this;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        ToolWindowChrome.SetStatusLeft(this, $"提醒事项：{_reminderItems.Count} 项");
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        ContentRendered += (_, _) =>
        {
            if (_notified) return;
            _notified = true;
            SystemSounds.Exclamation.Play();
            var flash = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                Flags = 3, Count = 3
            };
            FlashWindowEx(ref flash);
        };
    }

    public ObservableCollection<TotoItemRow> ReminderItems => _reminderItems;

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (Tasks.SelectedItem is not TotoItemRow row) return;
        if (new TotoItemWindow(_host, TotoItemWindowMode.Complete, row.Item) { Owner = this }.ShowDialog() != true) return;
        _reminderItems.Remove(row);
        ToolWindowChrome.SetStatusLeft(this, $"提醒事项：{_reminderItems.Count} 项");
        _host.Reschedule();
        ItemChanged?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
