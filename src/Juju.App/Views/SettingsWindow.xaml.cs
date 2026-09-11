using System.Windows;

namespace Juju.App;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;
    public SettingsWindow() { InitializeComponent(); Closed += (_, _) => _instance = null; }
    public static void ShowOrActivate() { _instance ??= new SettingsWindow(); if (!_instance.IsVisible) _instance.Show(); if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal; _instance.Activate(); }
}
