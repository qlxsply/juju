using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Juju.App.Bootstrap;
using Juju.Core.App;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Platform.Windows.HotKey;
using FormsControl = System.Windows.Forms.Control;
using FormsScreen = System.Windows.Forms.Screen;

namespace Juju.App;

public partial class LauncherWindow : Window
{
    private readonly IWindowManager _windows;
    private readonly ISettingsService _settings;
    private readonly GlobalShortcutService _shortcut = new();
    private readonly DispatcherTimer _timeout = new();
    private bool _armed;

    public LauncherWindow(IWindowManager windows, ISettingsService settings, ThemeService themes)
    {
        _windows = windows;
        _settings = settings;
        InitializeComponent();
        _timeout.Tick += (_, _) => Hide();
        IsVisibleChanged += (_, _) => { if (!IsVisible) _timeout.Stop(); };
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WindowProcedure);
            if (!_shortcut.Register(source.Handle, settings.Current.LeaderShortcut)) Title = $"juju (快捷键错误 {_shortcut.LastError})";
        };
        Deactivated += (_, _) => Hide();
    }

    public bool ApplySettings(AppSettings settings)
    {
        ShortcutText.Text = settings.LeaderShortcut;
        _timeout.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.LauncherTimeoutMilliseconds, 250, 30000));
        if (PresentationSource.FromVisual(this) is HwndSource source && !_shortcut.Register(source.Handle, settings.LeaderShortcut))
        {
            System.Windows.MessageBox.Show($"无法注册快捷键（Windows 错误码 {_shortcut.LastError}）。旧快捷键仍然有效。", "juju", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        PlaceOnPointerMonitor();
        _armed = Keyboard.Modifiers == ModifierKeys.None;
        ApplySettings(_settings.Current);
        Show(); Activate(); Focus();
        if (!_armed) _timeout.Start();
    }

    protected override void OnClosed(EventArgs e) { _shortcut.Dispose(); base.OnClosed(e); }
    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None) { _armed = true; _timeout.Stop(); }
        base.OnPreviewKeyUp(e);
    }
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.IsRepeat) { e.Handled = true; return; }
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }
        if (!_armed) { e.Handled = true; return; }
        if (e.Key == Key.D1) { _windows.ShowTool(ToolId.Json); Hide(); e.Handled = true; }
        else if (e.Key == Key.S) { _windows.ShowSettings(); Hide(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    private void PlaceOnPointerMonitor()
    {
        var area = FormsScreen.FromPoint(FormsControl.MousePosition).WorkingArea;
        Left = Math.Clamp(area.Left + (area.Width - Width) / 2d, area.Left, area.Right - Width);
        Top = Math.Clamp(area.Top + (area.Height - Height) / 2d, area.Top, area.Bottom - Height);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_shortcut.IsHotKeyMessage(message, wParam)) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }
}
