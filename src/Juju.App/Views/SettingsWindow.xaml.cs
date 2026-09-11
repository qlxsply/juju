using System.Windows;
using Juju.App.Bootstrap;
using Juju.Core.Settings;
using Juju.Platform.Windows.Startup;

namespace Juju.App;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settings;
    private readonly DataRootConfigurationService _roots;
    private readonly ThemeService _themes;
    private readonly LauncherWindow _launcher;
    private readonly IStartupService _startup = new StartupService();

    public SettingsWindow(ISettingsService settings, DataRootConfigurationService roots, ThemeService themes, LauncherWindow launcher)
    {
        _settings = settings; _roots = roots; _themes = themes; _launcher = launcher;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var current = _settings.Current;
        StartAtLogin.IsChecked = current.StartAtLogin; Shortcut.Text = current.LeaderShortcut; Timeout.Text = current.LauncherTimeoutMilliseconds.ToString();
        Theme.SelectedIndex = (int)current.Theme; DataRoot.Text = current.DataRoot; Autosave.Text = current.AutosaveDelayMilliseconds.ToString(); Indent.Text = current.JsonIndentSize.ToString();
    }
    private AppSettings ReadSettings() => _settings.Current with
    {
        StartAtLogin = StartAtLogin.IsChecked == true, LeaderShortcut = Shortcut.Text.Trim(),
        LauncherTimeoutMilliseconds = ReadNumber(Timeout, 250, 30000), Theme = (ThemePreference)Math.Max(0, Theme.SelectedIndex),
        DataRoot = DataRoot.Text.Trim(), AutosaveDelayMilliseconds = ReadNumber(Autosave, 100, 10000), JsonIndentSize = ReadNumber(Indent, 1, 8)
    };
    private static int ReadNumber(System.Windows.Controls.TextBox box, int minimum, int maximum) => int.TryParse(box.Text, out var value) && value >= minimum && value <= maximum ? value : throw new InvalidOperationException($"{box.Text} 不是有效范围内的数字。");
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = ReadSettings();
            var validation = await _roots.ValidateAsync(value.DataRoot, false, CancellationToken.None);
            if (!validation.IsValid) throw new InvalidOperationException(validation.Error);
            value = value with { DataRoot = validation.Path! };
            if (!_launcher.ApplySettings(value)) throw new InvalidOperationException("快捷键未保存，旧快捷键仍在使用。");
            _startup.SetEnabled(value.StartAtLogin); await _settings.SaveAsync(value); _themes.Apply(value.Theme); Status.Text = "已保存";
        }
        catch (Exception ex) { Status.Text = "保存失败: " + ex.Message; }
    }
    private async void UseDataRoot_Click(object sender, RoutedEventArgs e)
    {
        try { await _roots.UseAsync(DataRoot.Text.Trim(), CancellationToken.None); Status.Text = "DataRoot 已保存，下次启动时生效"; }
        catch (Exception ex) { Status.Text = "无法使用 DataRoot: " + ex.Message; }
    }
    private async void MigrateDataRoot_Click(object sender, RoutedEventArgs e)
    {
        try { await _roots.MigrateAsync(DataRoot.Text.Trim(), CancellationToken.None); Status.Text = "已迁移 DataRoot，下次启动时生效"; }
        catch (Exception ex) { Status.Text = "迁移失败: " + ex.Message; }
    }
}
