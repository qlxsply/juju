using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Juju.Platform.Windows.HotKey;

namespace Juju.App;

public partial class LauncherWindow : Window
{
    private readonly Action _openJson;
    private readonly Action _openSettings;
    private readonly GlobalShortcutService _shortcut = new();
    private bool _armed;

    public LauncherWindow(Action openJson, Action openSettings)
    {
        _openJson = openJson; _openSettings = openSettings;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WindowProcedure);
            _shortcut.Register(source.Handle);
        };
        Deactivated += (_, _) => Hide();
    }

    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        _armed = false;
        Show(); Activate(); Focus();
    }

    protected override void OnClosed(EventArgs e) { _shortcut.Dispose(); base.OnClosed(e); }
    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e) { if (Keyboard.Modifiers == ModifierKeys.None) _armed = true; base.OnPreviewKeyUp(e); }
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (!_armed) { base.OnPreviewKeyDown(e); return; }
        if (e.Key == Key.D1) { _openJson(); Hide(); e.Handled = true; }
        else if (e.Key == Key.S) { _openSettings(); Hide(); e.Handled = true; }
        else if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_shortcut.IsHotKeyMessage(message, wParam)) { Toggle(); handled = true; }
        return IntPtr.Zero;
    }
}
