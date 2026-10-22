using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Juju.App.Settings;
using Juju.Core.Tools;
using Juju.Platform.Windows;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;

namespace Juju.App.Views;

/// <summary>
/// XAML 构建左侧静态导航，DI 注入的 ISettingsSection 提供右侧业务页面。
/// 原型的前端状态 activeSection/toolsExpanded 对应下面的 _selectedId/_toolsExpanded。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly IReadOnlyDictionary<string, ISettingsSection> _sections;
    private readonly Dictionary<string, Button> _navigation;
    private string _selectedId = "system";
    private bool _appExpanded = true;
    private bool _toolsExpanded;
    private Action? _modalFocusReturn;
    private Func<string, Task>? _shortcutCommit;
    private Func<Task>? _modalCommit;
    private Func<string, string?>? _shortcutValidate;
    private string _shortcutDraft = "";
    private bool _shortcutAllowNone;
    private bool _shortcutAllowSingle;
    private bool _shortcutAllowUnmodified;
    private bool _captureSecondStroke;
    private bool _hotkeysSuspended;
    private LauncherWindow? _hotkeyOwner;
    public bool AllowClose { get; set; }

    public SettingsWindow(IEnumerable<ISettingsSection> sections, SettingsDialogService dialogs)
    {
        _sections = sections.ToDictionary(section => section.Id);
        InitializeComponent();
        dialogs.Attach(this);
        SourceInitialized += (_, _) => WindowAppearance.UseSystemCorners(new WindowInteropHelper(this).Handle);
        _navigation = new Dictionary<string, Button>
        {
            ["system"] = NavGeneral, ["shortcut"] = NavShortcut, ["storage"] = NavStorage,
            ["display"] = NavDisplay, ["about"] = NavAbout,
            ["json"] = NavJson, ["toto"] = NavToto, ["echo-http"] = NavEcho, ["kairos"] = NavKairos
        };
        Select("system");
        ExpandTools(true);
        // CheckBox 和 ComboBox 是标准 WPF 控件；路由事件在窗口集中触发即时保存。
        // TextBox 在失去键盘焦点时校验并提交；按键、路径和组合设置改用弹窗草稿事务。
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ImmediateSave));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ImmediateSave));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(SelectionChanged));
        AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(TextCommitted), handledEventsToo: true);
    }

    private void Select(string id)
    {
        if (!_sections.TryGetValue(id, out var section)) return;
        _selectedId = id;
        SectionContent.Content = section.View;
        ContentScroll.ScrollToTop();
        foreach (var (key, button) in _navigation) button.Tag = key == id ? "Active" : null;
        NavAppGroup.Tag = id is "system" or "shortcut" or "storage" or "display" or "about" ? "Active" : null;
        NavTools.Tag = id is "json" or "toto" or "echo-http" or "kairos" ? "Active" : null;
    }

    public void NavigateToGeneral()
    {
        ExpandApp(true);
        Select("system");
    }

    public void NavigateToTool(string id)
    {
        ExpandTools(true);
        Select(id);
    }

    /// <summary>打开按键录制弹窗；initial 值被复制到草稿，只有提交委托成功后才应用设置。</summary>
    public void OpenShortcutDialog(string title, string current, bool allowNone, bool allowSingle,
        bool allowSequence, Func<string, string?> validate, Func<string, Task> commit, string? defaultValue = null,
        bool allowUnmodified = false)
    {
        var previousFocus = Keyboard.FocusedElement;
        _shortcutDraft = current;
        _shortcutAllowNone = allowNone;
        _shortcutAllowSingle = allowSingle;
        _shortcutAllowUnmodified = allowUnmodified;
        _shortcutValidate = validate;
        _shortcutCommit = commit;
        _captureSecondStroke = false;
        _hotkeyOwner = Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
        _hotkeyOwner?.SetShortcutCapture(true);
        _hotkeysSuspended = _hotkeyOwner is not null;
        ModalDialog.Width = 460;
        ModalTitle.Text = title;
        // ModalDescription.Text = "按下实际组合键进行录制；Backspace 清除当前录制。取消不会修改原设置。";
        var body = new StackPanel();
        var capture = new Border
        {
            MinHeight = 95, BorderBrush = (Brush)Application.Current.Resources["ToolboxLine"],
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Background = (Brush)new BrushConverter().ConvertFromString("#FCFDFF")!,
            Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Focusable = true,
            Child = new StackPanel()
        };
        var keyPanel = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        _modalKeyPanel = keyPanel;
        capture.Child = keyPanel;
        capture.PreviewMouseLeftButtonDown += (_, _) => capture.Focus();
        body.Children.Add(capture);
        var hint = new TextBlock
        {
            // Text = allowSequence ? "点击“追加第二段”后录入第二组键。修改只保存为弹窗草稿。" :
            //     "修改只保存为弹窗草稿；只有点击“保存”才注册并持久化快捷键。",
            FontSize = 11, Foreground = (Brush)Application.Current.Resources["ToolboxMuted"],
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        body.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var clear = SettingsViewFactory.SoftButton("清除", (_, _) =>
        {
            _shortcutDraft = allowNone ? GlobalHotkeyPolicy.None : "";
            _captureSecondStroke = false;
            RenderShortcutDraft(keyPanel);
        });
        clear.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(clear);
        if (defaultValue is not null)
        {
            var restore = SettingsViewFactory.SoftButton("恢复默认", (_, _) =>
            {
                _shortcutDraft = defaultValue;
                _captureSecondStroke = false;
                RenderShortcutDraft(keyPanel);
            });
            restore.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(restore);
        }
        if (allowSequence)
            actions.Children.Add(SettingsViewFactory.SoftButton("追加第二段", (_, _) =>
            {
                _captureSecondStroke = true;
                hint.Text = "现在按下第二组组合键。";
                capture.Focus();
            }));
        body.Children.Add(actions);
        var error = new TextBlock { Margin = new Thickness(0, 9, 0, 0),
            Foreground = (Brush)new BrushConverter().ConvertFromString("#BD3434")!,
            FontSize = 11, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(error);
        ModalBody.Content = body;
        _modalCommit = async () => await commit(_shortcutDraft);
        _modalFocusReturn = () => { if (previousFocus is IInputElement target) Keyboard.Focus(target); else Focus(); };
        _modalError = error;
        RenderShortcutDraft(keyPanel);
        ModalSave.Content = "保存";
        ModalOverlay.Visibility = Visibility.Visible;
        ModalOverlay.Focus();
        capture.Focus();
    }

    public void OpenEditorDialog(string title, string description, FrameworkElement draftContent, Func<Task> commit,
        double width = 460, string saveText = "保存")
    {
        var previousFocus = Keyboard.FocusedElement;
        _shortcutCommit = null;
        _modalCommit = commit;
        _shortcutValidate = null;
        _modalKeyPanel = null;
        _modalError = null;
        ModalTitle.Text = title;
        ModalDescription.Text = description;
        ModalDialog.Width = width;
        ModalBody.Content = draftContent;
        _modalFocusReturn = () => { if (previousFocus is IInputElement target) Keyboard.Focus(target); else Focus(); };
        ModalSave.Content = saveText;
        ModalSave.IsEnabled = true;
        ModalOverlay.Visibility = Visibility.Visible;
        ModalOverlay.Focus();
        draftContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private TextBlock? _modalError;
    private StackPanel? _modalKeyPanel;

    private void RenderShortcutDraft(Panel panel)
    {
        panel.Children.Clear();
        var draft = _shortcutDraft;
        if (string.IsNullOrWhiteSpace(draft) || draft == GlobalHotkeyPolicy.None)
            panel.Children.Add(new TextBlock { Text = "按下快捷键组合", Foreground =
                (Brush)Application.Current.Resources["ToolboxMuted"], VerticalAlignment = VerticalAlignment.Center });
        else
        {
            var strokes = draft.Split(' ');
            for (var index = 0; index < strokes.Length; index++)
            {
                if (index > 0) panel.Children.Add(new TextBlock { Text = "→", Margin = new Thickness(4),
                    VerticalAlignment = VerticalAlignment.Center, Foreground =
                        (Brush)Application.Current.Resources["ToolboxMuted"] });
                foreach (var key in strokes[index].Split('+'))
                    panel.Children.Add(new Border
                    {
                        Background = (Brush)new BrushConverter().ConvertFromString("#EBF1FF")!,
                        BorderBrush = (Brush)new BrushConverter().ConvertFromString("#DCE7FF")!,
                        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(2),
                        Child = new TextBlock { Text = key, FontSize = 12, Foreground =
                            (Brush)new BrushConverter().ConvertFromString("#466EC9")! }
                    });
            }
        }
        _modalError!.Text = ShortcutError();
        ModalSave.IsEnabled = string.IsNullOrEmpty(_modalError.Text);
    }

    private void ModalOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ModalOverlay.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            CancelModal_Click(this, new RoutedEventArgs());
            return;
        }
        if (_shortcutCommit is null) return;
        e.Handled = true;
        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            _shortcutDraft = _shortcutAllowNone ? GlobalHotkeyPolicy.None : "";
            _captureSecondStroke = false;
            if (_modalKeyPanel is not null) RenderShortcutDraft(_modalKeyPanel);
            return;
        }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin) return;
        var stroke = GetShortcutStroke(e);
        if (stroke is null) return;
        if (_captureSecondStroke)
        {
            var first = _shortcutDraft == GlobalHotkeyPolicy.None ? "" : _shortcutDraft.Split(' ')[0];
            _shortcutDraft = string.IsNullOrEmpty(first) ? stroke : first + " " + stroke;
            _captureSecondStroke = false;
        }
        else _shortcutDraft = stroke;
        if (_modalKeyPanel is not null) RenderShortcutDraft(_modalKeyPanel);
    }

    private static string? GetShortcutStroke(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var name = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)key - (int)Key.NumPad0).ToString(),
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space", Key.Return => "Enter", Key.Tab => "Tab", Key.Escape => "Escape",
            Key.Back => "Backspace", Key.Delete => "Delete",
            Key.Home => "Home", Key.End => "End", Key.Prior => "PageUp", Key.Next => "PageDown",
            Key.Up => "Up", Key.Down => "Down", Key.Left => "Left", Key.Right => "Right",
            Key.Insert => "Insert", Key.OemPlus => "Oemplus", Key.OemMinus => "OemMinus",
            Key.OemComma => "Oemcomma", Key.OemPeriod => "OemPeriod", Key.OemQuestion => "OemQuestion",
            Key.OemTilde => "Oemtilde", Key.OemOpenBrackets => "OemOpenBrackets",
            Key.OemCloseBrackets => "OemCloseBrackets", Key.OemPipe => "OemPipe",
            Key.OemQuotes => "OemQuotes", Key.OemSemicolon => "OemSemicolon", Key.OemBackslash => "OemBackslash",
            Key.Add => "Add", Key.Subtract => "Subtract", Key.Multiply => "Multiply", Key.Divide => "Divide",
            Key.Decimal => "Decimal", _ => null
        };
        if (name is null) return null;
        var modifiers = Keyboard.Modifiers;
        return string.Join('+', new[]
        {
            modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            modifiers.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            modifiers.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            modifiers.HasFlag(ModifierKeys.Windows) ? "Win" : null,
            name
        }.Where(part => part is not null));
    }

    private void CancelModal_Click(object sender, RoutedEventArgs e)
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
        ModalBody.Content = null;
        _shortcutCommit = null;
        _modalCommit = null;
        _modalKeyPanel = null;
        if (_hotkeysSuspended)
        {
            _hotkeysSuspended = false;
            _hotkeyOwner?.SetShortcutCapture(false);
        }
        _hotkeyOwner = null;
        _modalFocusReturn?.Invoke();
    }

    private async void SaveModal_Click(object sender, RoutedEventArgs e)
    {
        if (_modalCommit is null) return;
        var error = _shortcutCommit is null ? "" : ShortcutError();
        if (!string.IsNullOrEmpty(error)) { if (_modalError is not null) _modalError.Text = error; return; }
        ModalSave.IsEnabled = false;
        try
        {
            await _modalCommit();
            CancelModal_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            if (_modalError is not null) _modalError.Text = ex.Message;
            ModalSave.IsEnabled = true;
        }
    }

    private string ShortcutError()
    {
        if (string.IsNullOrEmpty(_shortcutDraft)) return _shortcutAllowNone ? "" : "此项必须设置快捷键。";
        if (_shortcutDraft == GlobalHotkeyPolicy.None) return _shortcutAllowNone ? "" : "此项不能清除。";
        if (_shortcutAllowSingle && (_shortcutDraft.Length != 1 || !char.IsLetterOrDigit(_shortcutDraft[0])))
            return "工具入口单键必须是一个字母或数字。";
        if (!_shortcutAllowSingle && !_shortcutAllowUnmodified && _shortcutDraft.Split(' ').Any(stroke => !stroke.Contains('+')))
            return "请使用修饰键和主键组合。";
        return _shortcutValidate?.Invoke(_shortcutDraft) ?? "";
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string id }) return;
        if (id is "system" or "shortcut" or "storage" or "display" or "about") ExpandApp(true);
        if (id is "json" or "toto" or "echo-http" or "kairos") ExpandTools(true);
        Select(id);
    }

    private void AppGroup_Click(object sender, RoutedEventArgs e) => ExpandApp(!_appExpanded);

    private void ToolGroup_Click(object sender, RoutedEventArgs e)
    {
        ExpandTools(!_toolsExpanded);
    }

    private void ExpandApp(bool expanded)
    {
        _appExpanded = expanded;
        AppChildren.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AppChevron.RenderTransform = new RotateTransform(expanded ? 180 : 0);
    }

    private void ExpandTools(bool expanded)
    {
        _toolsExpanded = expanded;
        ToolChildren.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ToolChevron.RenderTransform = new RotateTransform(expanded ? 180 : 0);
    }

    private async void ImmediateSave(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source || !_sections.TryGetValue(_selectedId, out var section)) return;
        await SaveImmediateAsync(section, source);
    }

    private async void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is ComboBox && _selectedId is "display" or "system" &&
            _sections.TryGetValue(_selectedId, out var section))
            await SaveImmediateAsync(section, (FrameworkElement)e.OriginalSource);
    }

    private async void TextCommitted(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is not TextBox textBox || !textBox.IsEnabled ||
            !_sections.TryGetValue(_selectedId, out var section)) return;
        await SaveImmediateAsync(section, textBox);
    }

    private async Task SaveImmediateAsync(ISettingsSection section, FrameworkElement source)
    {
        try { await section.SaveImmediateAsync(source); }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败，设置未更改：" + ex.Message, "Toolbox 设置",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource == sender) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    // 普通关闭保留已有控件输入和设置导航；Juju 退出时由 DesktopWindowManager 设置 AllowClose。
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ModalOverlay.Visibility == Visibility.Visible) CancelModal_Click(this, new RoutedEventArgs());
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
