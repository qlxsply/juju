using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Juju.App.Settings;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

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
    private bool _toolsExpanded;
    private bool _saving;
    private bool _saveAgain;
    public bool AllowClose { get; set; }

    public SettingsWindow(IEnumerable<ISettingsSection> sections)
    {
        _sections = sections.ToDictionary(section => section.Id);
        InitializeComponent();
        _navigation = new Dictionary<string, Button>
        {
            ["system"] = NavGeneral, ["shortcut"] = NavShortcut, ["storage"] = NavStorage,
            ["display"] = NavDisplay, ["about"] = NavAbout,
            ["json"] = NavJson, ["toto"] = NavToto
        };
        Select("system");
        // CheckBox 和 ComboBox 是标准 WPF 控件；路由事件在窗口集中触发即时保存。
        // TextBox 不监听每个字符：用户输入完热键后使用“保存更改”，避免半输入状态注册失败。
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(ImmediateSave));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(ImmediateSave));
        AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(SelectionChanged));
    }

    private void Select(string id)
    {
        if (!_sections.TryGetValue(id, out var section)) return;
        _selectedId = id;
        SectionContent.Content = section.View;
        ContentScroll.ScrollToTop();
        foreach (var (key, button) in _navigation) button.Tag = key == id ? "Active" : null;
        NavTools.Tag = id is "json" or "toto" ? "Active" : null;
        SaveButton.Visibility = id is "storage" or "about" or "toto" ? Visibility.Collapsed : Visibility.Visible;
        Status.Text = id is "storage" or "about" or "toto" ? "该页面无需保存" :
            "更改开关后自动保存；文本输入请点击保存";
    }

    public void NavigateToGeneral()
    {
        ExpandTools(false);
        Select("system");
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string id }) return;
        if (id is "json" or "toto") ExpandTools(true);
        Select(id);
    }

    private void ToolGroup_Click(object sender, RoutedEventArgs e)
    {
        ExpandTools(!_toolsExpanded);
        Select(_toolsExpanded ? "json" : "system");
    }

    private void ExpandTools(bool expanded)
    {
        _toolsExpanded = expanded;
        ToolChildren.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ToolChevron.Text = expanded ? "⌄" : "›";
    }

    private async void ImmediateSave(object sender, RoutedEventArgs e)
    {
        if (_selectedId is "system" or "display" or "json") await SaveCurrentAsync();
    }

    private async void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is ComboBox && _selectedId == "display") await SaveCurrentAsync();
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();

    private async Task SaveCurrentAsync()
    {
        if (_saving) { _saveAgain = true; return; }
        _saving = true;
        SaveButton.IsEnabled = false;
        try
        {
            do
            {
                _saveAgain = false;
                var section = _sections[_selectedId];
                await section.SaveAsync();
            } while (_saveAgain);
            Status.Text = "已保存";
        }
        catch (Exception ex)
        {
            Status.Text = "保存失败：" + ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            _saving = false;
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
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
