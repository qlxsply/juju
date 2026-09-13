using System.Windows;
using Juju.App.Settings;

namespace Juju.App;

public partial class SettingsWindow : Window
{
    private readonly IReadOnlyList<ISettingsSection> _sections;
    public bool AllowClose { get; set; }

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

    private void SettingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SettingsTreeNode { Section: { } section }) SectionContent.Content = section.View;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try { if (SettingsTree.SelectedItem is SettingsTreeNode { Section: { } section }) await section.SaveAsync(); Status.Text = "已保存"; }
        catch (Exception ex) { Status.Text = "保存失败: " + ex.Message; }
    }

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

// Public because SettingsWindow.xaml's HierarchicalDataTemplate binds to this type.
public sealed class SettingsTreeNode
{
    public SettingsTreeNode(string name, IEnumerable<ISettingsSection> sections) { Name = name; Children = sections.Select(section => new SettingsTreeNode(section.DisplayName, section)).ToArray(); }
    private SettingsTreeNode(string name, ISettingsSection section) { Name = name; Section = section; }
    public string Name { get; }
    public bool IsExpanded => Children.Count > 0;
    public ISettingsSection? Section { get; }
    public IReadOnlyList<SettingsTreeNode> Children { get; } = [];
    public override string ToString() => Name;
}
