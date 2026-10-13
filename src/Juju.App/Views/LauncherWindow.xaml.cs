using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Juju.App.Bootstrap;
using Juju.Core.App;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.Platform.Windows.HotKey;
using Toto.App;
using Button = System.Windows.Controls.Button;
using FormsControl = System.Windows.Forms.Control;
using FormsScreen = System.Windows.Forms.Screen;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Views;

// 对应 XAML 的 partial 代码隐藏类；InitializeComponent 会创建并命名 XAML 中声明的元素。
public partial class LauncherWindow : Window
{
    private readonly IWindowManager _windows;
    private readonly ISettingsService _settings;
    private readonly GlobalShortcutService _shortcut = new();
    private readonly GlobalShortcutService _jsonShortcut = new();
    private readonly GlobalShortcutService _totoShortcut = new();
    private readonly TotoToolHost _toto;
    private readonly DispatcherTimer _timeout = new();
    private bool _armed;

    // 构造函数依赖由窗口管理器传入；ThemeService 参数用于保持依赖图一致，主题资源由 WPF 全局资源解析。
    public LauncherWindow(IWindowManager windows, ISettingsService settings, ThemeService themes, TotoToolHost toto)
    {
        _windows = windows;
        _settings = settings;
        _toto = toto;
        InitializeComponent();
        // DispatcherTimer 在 WPF UI 线程触发，不需要额外 Dispatcher.Invoke 即可安全隐藏窗口。
        _timeout.Tick += (_, _) => Hide();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _timeout.Stop();
        };
        // SourceInitialized 表示 Window 已拥有 Win32 HWND；全局热键和窗口过程钩子只能在此之后注册。
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            source.AddHook(WindowProcedure);
            if (!_shortcut.Register(source.Handle, settings.Current.LeaderShortcut))
                Title = $"juju (快捷键错误 {_shortcut.LastError})";
            RegisterToolHotkey(_jsonShortcut, settings.Current.JsonShortcut, "JSON");
            RegisterToolHotkey(_totoShortcut, _toto.Hotkey, "Toto");
        };
        _toto.HotkeyChanged += OnTotoHotkeyChanged;
        _toto.HotkeyChanging += TryRegisterTotoHotkey;
        // 失焦自动隐藏使 Launcher 表现为临时命令面板，而不是常驻普通窗口。
        Deactivated += (_, _) => Hide();
    }

    // 重注册失败时保留旧热键，避免一次无效配置让用户完全失去唤起入口。
    public bool ApplySettings(AppSettings settings)
    {
        _timeout.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.LauncherTimeoutMilliseconds, 250, 30000));
        if (PresentationSource.FromVisual(this) is HwndSource source &&
            !_shortcut.Register(source.Handle, settings.LeaderShortcut))
        {
            MessageBox.Show($"无法注册快捷键（Windows 错误码 {_shortcut.LastError}）。旧快捷键仍然有效。", "juju",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return PresentationSource.FromVisual(this) is not HwndSource ||
               RegisterToolHotkey(_jsonShortcut, settings.JsonShortcut, "JSON");
    }

    private bool TryRegisterTotoHotkey(string hotkey) => RegisterToolHotkey(_totoShortcut, hotkey, "Toto");
    private void OnTotoHotkeyChanged(string hotkey) => RegisterToolHotkey(_totoShortcut, hotkey, "Toto");

    private bool RegisterToolHotkey(GlobalShortcutService service, string hotkey, string name)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source) return false;
        if (service.Register(source.Handle, hotkey)) return true;
        MessageBox.Show($"{name} 全局快捷键 {hotkey} 无法注册（Windows 错误码 {service.LastError}），原快捷键仍然有效。",
            "juju", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    // 显示前先按鼠标所在显示器定位，随后激活窗口以接收键盘预览事件。
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        PlaceOnPointerMonitor();
        _armed = Keyboard.Modifiers == ModifierKeys.None;
        ApplySettings(_settings.Current);
        Show();
        Activate();
        Focus();
        if (!_armed) _timeout.Start();
    }

    // Window 真正关闭（而不是 Hide）时释放底层 Win32 热键注册。
    protected override void OnClosed(EventArgs e)
    {
        _shortcut.Dispose();
        _jsonShortcut.Dispose();
        _totoShortcut.Dispose();
        _toto.HotkeyChanged -= OnTotoHotkeyChanged;
        _toto.HotkeyChanging -= TryRegisterTotoHotkey;
        base.OnClosed(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            _armed = true;
            _timeout.Stop();
        }

        base.OnPreviewKeyUp(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (!_armed)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D1 || e.Key == Key.D2)
        {
            _windows.ShowTool(e.Key == Key.D1 ? ToolId.Json : ToolId.Toto);
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.S)
        {
            _windows.ShowSettings();
            Hide();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    private void PlaceOnPointerMonitor()
    {
        var area = FormsScreen.FromPoint(FormsControl.MousePosition).WorkingArea;
        Left = Math.Clamp(area.Left + (area.Width - Width) / 2d, area.Left, area.Right - Width);
        Top = Math.Clamp(area.Top + (area.Height - Height) / 2d, area.Top, area.Bottom - Height);
    }

    // 自定义标题栏的 Grid 是普通 WPF 客户区控件，并不是 Windows 原生标题栏。
    // DragMove 告诉 WPF/Win32：从当前鼠标位置开始执行系统窗口拖动，因此仍可获得贴靠等系统行为。
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // OriginalSource == sender 只允许标题栏空白处发起拖动，防止点击设置按钮或其他子控件时移动窗口。
        // LeftButton 检查排除右键等其他鼠标输入；DragMove 必须在按住左键时调用。
        if (e.OriginalSource == sender && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _windows.ShowSettings();
        Hide();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value } ||
            !Enum.TryParse<ToolId>(value, out var tool)) return;
        if (tool is ToolId.Json or ToolId.Toto)
        {
            _windows.ShowTool(tool);
            Hide();
            return;
        }

        MessageBox.Show($"{value} 正在开发中。", "juju", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // HwndSourceHook 是 WPF 到原生 Win32 消息循环的桥梁；处理热键消息后标记 handled 阻止继续分发。
    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_shortcut.IsHotKeyMessage(message, wParam))
        {
            Toggle();
            handled = true;
        }
        else if (_jsonShortcut.IsHotKeyMessage(message, wParam))
        {
            _windows.ShowTool(ToolId.Json);
            handled = true;
        }
        else if (_totoShortcut.IsHotKeyMessage(message, wParam))
        {
            _windows.ShowTool(ToolId.Toto);
            handled = true;
        }

        return IntPtr.Zero;
    }
}
