using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Juju.App.Bootstrap;
using Juju.App.Contracts.Windows;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.App.Tools.Contracts;
using Juju.Tools.Toto.Application;
using Juju.Tools.Json.Settings;
using Juju.Platform.Windows.HotKey;
using Juju.Platform.Windows;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Views;

// 对应 XAML 的 partial 代码隐藏类；InitializeComponent 会创建并命名 XAML 中声明的元素。
public partial class LauncherWindow : Window
{
    private readonly IAppWindowManager _windows;
    private readonly IToolManager _tools;
    private readonly ISettingsService _settings;
    private readonly GlobalShortcutService _shortcut = new();
    private readonly GlobalShortcutService _jsonShortcut = new();
    private readonly GlobalShortcutService _totoShortcut = new();
    private readonly Dictionary<ToolId, GlobalShortcutService> _extraShortcuts = new()
    {
        [ToolId.EchoHttp] = new(), [ToolId.Kairos] = new()
    };
    private readonly ITotoApplicationService _toto;
    private readonly IJsonToolSettingsService _jsonSettings;
    private readonly DispatcherTimer _timeout = new();
    private bool _armed;
    private bool _settingsOpen;
    private int _shortcutCaptureDepth;
    public double ToolRowHeight { get; private set; } = 53;

    // 构造函数依赖由窗口管理器传入；ThemeService 参数用于保持依赖图一致，主题资源由 WPF 全局资源解析。
    public LauncherWindow(IToolManager tools, IAppWindowManager windows, ISettingsService settings,
        ThemeService themes, ITotoApplicationService toto, IJsonToolSettingsService jsonSettings)
    {
        _tools = tools;
        _windows = windows;
        _settings = settings;
        _toto = toto;
        _jsonSettings = jsonSettings;
        InitializeComponent();
        DataContext = this;
        // DispatcherTimer 在 WPF UI 线程触发，不需要额外 Dispatcher.Invoke 即可安全隐藏窗口。
        _timeout.Tick += (_, _) => { if (!_settingsOpen) Hide(); };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) _timeout.Stop();
        };
        // SourceInitialized 表示 Window 已拥有 Win32 HWND；全局热键和窗口过程钩子只能在此之后注册。
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            WindowAppearance.UseSystemCorners(source.Handle);
            source.AddHook(WindowProcedure);
            if (!_shortcut.Register(source.Handle, settings.Current.LeaderShortcut))
                Title = $"juju (快捷键错误 {_shortcut.LastError})";
            TryRegisterToolHotkey(ToolId.Json, _jsonSettings.Current.GlobalShortcut);
            TryRegisterToolHotkey(ToolId.Toto, _toto.GlobalHotkey);
            foreach (var (tool, _) in _extraShortcuts)
                TryRegisterToolHotkey(tool, settings.Current.ToolGlobalShortcuts.GetValueOrDefault(tool.ToString(),
                    GlobalHotkeyPolicy.None));
        };
        _toto.HotkeyChanged += OnTotoHotkeyChanged;
        _toto.HotkeyChanging += TryRegisterTotoHotkey;
        // 设置窗口打开时入口留在后方；其余情况下由用户偏好决定是否失焦自动隐藏。
        Deactivated += (_, _) =>
        {
            if (!_settingsOpen && _settings.Current.AutoHideLauncher) Hide();
        };
    }

    // 重注册失败时保留旧热键，避免一次无效配置让用户完全失去唤起入口。
    public bool ApplySettings(AppSettings settings)
    {
        var keys = ToolLaunchKeys.Normalize(settings.ToolKeys);
        try
        {
            GlobalHotkeyPolicy.EnsureDistinct(settings.LeaderShortcut,
                new[] { _jsonSettings.Current.GlobalShortcut, _toto.GlobalHotkey }
                    .Concat(_extraShortcuts.Keys.Select(tool =>
                        settings.ToolGlobalShortcuts.GetValueOrDefault(tool.ToString()))).ToArray());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "juju", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var source = PresentationSource.FromVisual(this) as HwndSource;
        var registered = source is null || (_shortcutCaptureDepth > 0
            ? RegisterTransient(_shortcut, source.Handle, settings.LeaderShortcut)
            : _shortcut.Register(source.Handle, settings.LeaderShortcut));
        if (!registered)
        {
            MessageBox.Show($"无法注册快捷键（Windows 错误码 {_shortcut.LastError}）。旧快捷键仍然有效。", "juju",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        // 只有所有验证/注册都成功后才更新屏幕状态；失败时界面与持久化值保持一致。
        _timeout.Interval = TimeSpan.FromMilliseconds(Math.Clamp(settings.LauncherTimeoutMilliseconds, 250, 30000));
        ToolRowHeight = settings.CompactLauncher ? 45 : 53;
        ToolRows.ItemsSource = _tools.Catalog.All.Select(tool => tool with
        {
            DefaultLauncherKey = keys[tool.Id.ToString()]
        }).ToArray();
        return true;
    }

    private bool TryRegisterTotoHotkey(string hotkey) => TryRegisterToolHotkey(ToolId.Toto, hotkey);
    private void OnTotoHotkeyChanged(string hotkey) => TryRegisterToolHotkey(ToolId.Toto, hotkey);

    /// <summary>设置对话框盖在 Launcher 上时禁止失焦、组合键残留与超时将入口隐藏。</summary>
    public void SetSettingsOpen(bool value)
    {
        _settingsOpen = value;
        if (value) _timeout.Stop();
    }

    /// <summary>录制快捷键期间暂时注销操作系统热键，避免 WM_HOTKEY 抢走用户正在录制的按键。</summary>
    public void SetShortcutCapture(bool capturing)
    {
        if (capturing)
        {
            if (_shortcutCaptureDepth++ == 0) ClearAllGlobalHotkeys();
            return;
        }
        if (_shortcutCaptureDepth == 0) return;
        if (--_shortcutCaptureDepth == 0) RestoreAllGlobalHotkeys();
    }

    private void ClearAllGlobalHotkeys()
    {
        _shortcut.Clear();
        _jsonShortcut.Clear();
        _totoShortcut.Clear();
        foreach (var service in _extraShortcuts.Values) service.Clear();
    }

    private void RestoreAllGlobalHotkeys()
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null) return;
        if (!_shortcut.Register(source.Handle, _settings.Current.LeaderShortcut))
            MessageBox.Show($"工具箱入口热键无法恢复（Windows 错误码 {_shortcut.LastError}）。", "juju",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        TryRegisterToolHotkey(ToolId.Json, _jsonSettings.Current.GlobalShortcut);
        TryRegisterToolHotkey(ToolId.Toto, _toto.GlobalHotkey);
        foreach (var tool in _extraShortcuts.Keys)
            TryRegisterToolHotkey(tool, _settings.Current.ToolGlobalShortcuts.GetValueOrDefault(
                tool.ToString(), GlobalHotkeyPolicy.None));
    }

    private static bool RegisterTransient(GlobalShortcutService service, IntPtr hwnd, string shortcut)
    {
        var registered = service.Register(hwnd, shortcut);
        service.Clear();
        return registered;
    }

    public bool TryRegisterToolHotkey(ToolId tool, string hotkey)
    {
        var name = tool.ToString();
        var service = tool switch
        {
            ToolId.Json => _jsonShortcut, ToolId.Toto => _totoShortcut,
            _ when _extraShortcuts.TryGetValue(tool, out var extra) => extra,
            _ => null
        };
        if (service is null) return false;
        string normalized;
        try
        {
            normalized = GlobalHotkeyPolicy.Normalize(hotkey, optional: true);
            GlobalHotkeyPolicy.EnsureDistinct(_settings.Current.LeaderShortcut,
                new[] { tool == ToolId.Json ? normalized : _jsonSettings.Current.GlobalShortcut,
                        tool == ToolId.Toto ? normalized : _toto.GlobalHotkey }
                    .Concat(_extraShortcuts.Keys.Select(item => item == tool ? normalized :
                        _settings.Current.ToolGlobalShortcuts.GetValueOrDefault(item.ToString()))).ToArray());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "juju", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (normalized == GlobalHotkeyPolicy.None) { service.Clear(); return true; }
        if (PresentationSource.FromVisual(this) is not HwndSource source) return false;
        if (_shortcutCaptureDepth > 0) return RegisterTransient(service, source.Handle, normalized);
        if (service.Register(source.Handle, normalized)) return true;
        MessageBox.Show($"{name} 全局快捷键 {normalized} 无法注册（Windows 错误码 {service.LastError}），原快捷键仍然有效。",
            "juju", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    // 显示时保留用户拖动过的位置；首次启动由 XAML 的 CenterScreen 决定默认位置。
    public void Toggle()
    {
        // 与原型 toggleLauncher 一致：设置盖在入口上时，唤醒键先收起设置而不是隐藏整个入口。
        if (_settingsOpen)
        {
            System.Windows.Application.Current.Windows.OfType<SettingsWindow>()
                .FirstOrDefault(window => window.IsVisible)?.Close();
            return;
        }
        if (IsVisible)
        {
            Hide();
            return;
        }

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
        foreach (var service in _extraShortcuts.Values) service.Dispose();
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
        if (e.IsRepeat && e.Key is not (Key.Up or Key.Down))
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

        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down)
        {
            MoveToolFocus(e.Key == Key.Down);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None &&
            e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9)
        {
            var pressed = e.Key is >= Key.D0 and <= Key.D9 ? e.Key.ToString()[1..] : e.Key.ToString();
            var match = _settings.Current.ToolKeys.FirstOrDefault(entry =>
                string.Equals(entry.Value, pressed, StringComparison.OrdinalIgnoreCase));
            if (Enum.TryParse<ToolId>(match.Key, out var tool))
            {
                OpenTool(tool);
                e.Handled = true;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void MoveToolFocus(bool down)
    {
        var rows = Enumerable.Range(0, ToolRows.Items.Count)
            .Select(index => FindVisualChild<Button>(ToolRows.ItemContainerGenerator.ContainerFromIndex(index)))
            .Where(row => row is not null).Cast<Button>().ToArray();
        if (rows.Length == 0) return;
        var current = Array.FindIndex(rows, row => row.IsKeyboardFocusWithin);
        var next = current < 0
            ? down ? 0 : rows.Length - 1
            : (current + (down ? 1 : -1) + rows.Length) % rows.Length;
        Keyboard.Focus(rows[next]);
    }

    // 自定义标题栏的 Grid 是普通 WPF 客户区控件；DragMove 使用系统窗口移动操作。
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 标题、图标和空白区域均可拖动，唯独设置按钮应接收自己的点击事件。
        if (e.LeftButton == MouseButtonState.Pressed && !SettingsButton.IsMouseOver) DragMove();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _windows.ShowSettings();
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ToolId tool }) return;
        OpenTool(tool);
    }

    private void OpenTool(ToolId tool)
    {
        _tools.Open(tool);
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        if (parent is T match) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindVisualChild<T>(VisualTreeHelper.GetChild(parent, index)) is { } child)
                return child;
        return null;
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
            _tools.Open(ToolId.Json);
            handled = true;
        }
        else if (_totoShortcut.IsHotKeyMessage(message, wParam))
        {
            _tools.Open(ToolId.Toto);
            handled = true;
        }
        else
            foreach (var (tool, service) in _extraShortcuts)
                if (service.IsHotKeyMessage(message, wParam))
                {
                    OpenTool(tool);
                    handled = true;
                    break;
                }

        return IntPtr.Zero;
    }
}
