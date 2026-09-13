using System.Windows;
using Juju.App.Settings;

namespace Juju.App.Views;

// SettingsWindow.xaml 编译后会生成 InitializeComponent；partial 将生成的控件字段与此处逻辑合并。
// 这类似 Java 的 FXML 控制器绑定，但 WPF 使用编译期生成的强类型 partial 类。
public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<ISettingsSection> _sections;
    public bool AllowClose { get; set; }

    // IEnumerable 由 DI 注入，容器会收集所有 ISettingsSection 注册项，使新增设置页无需修改窗口。
    public SettingsWindow(IEnumerable<ISettingsSection> sections)
    {
        _sections = sections.ToArray();
        InitializeComponent();
        var roots = new[]
        {
            new SettingsTreeNode("基础", _sections.Where(section => section.Id == "system")),
            new SettingsTreeNode("工具", _sections.Where(section => section.Id != "system"))
        };
        SettingsTree.ItemsSource = roots;
        SectionContent.Content = _sections.FirstOrDefault(section => section.Id == "system")?.View;
    }

    // XAML 事件处理器运行在 Dispatcher/UI 线程；依据新选择项替换右侧承载的 View。
    private void SettingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SettingsTreeNode { Section: { } section }) SectionContent.Content = section.View;
    }

    // Button.Click 的委托要求 void，所以 async void 仅用于事件边界；保存异常在此转换为界面状态。
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SettingsTree.SelectedItem is SettingsTreeNode { Section: { } section }) await section.SaveAsync();
            Status.Text = "已保存";
        }
        catch (Exception ex)
        {
            Status.Text = "保存失败: " + ex.Message;
        }
    }

    // 普通关闭改为隐藏，从而保留控件输入；应用显式关停时由管理器设置 AllowClose 释放窗口。
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}

// 必须为 public，因为 SettingsWindow.xaml 的 HierarchicalDataTemplate 需要在运行时访问此类型。
// 树节点是纯展示模型，不承担保存行为；叶节点才保留实际的设置节。
public sealed class SettingsTreeNode
{
    public SettingsTreeNode(string name, IEnumerable<ISettingsSection> sections)
    {
        Name = name;
        Children = sections.Select(section => new SettingsTreeNode(section.DisplayName, section)).ToArray();
    }

    private SettingsTreeNode(string name, ISettingsSection section)
    {
        Name = name;
        Section = section;
    }

    public string Name { get; }
    public bool IsExpanded => Children.Count > 0;
    public ISettingsSection? Section { get; }
    public IReadOnlyList<SettingsTreeNode> Children { get; } = [];
    public override string ToString() => Name;
}