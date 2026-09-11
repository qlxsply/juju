using Microsoft.Win32;
using System.Windows;
using Juju.Core.Settings;

namespace Juju.App.Bootstrap;

public enum EffectiveTheme { Light, Dark }

public sealed class ThemeService
{
    public event Action<EffectiveTheme>? Changed;
    public EffectiveTheme Current { get; private set; } = EffectiveTheme.Light;

    public void Apply(ThemePreference preference)
    {
        var theme = preference == ThemePreference.System ? GetSystemTheme() : preference == ThemePreference.Dark ? EffectiveTheme.Dark : EffectiveTheme.Light;
        Current = theme;
        var resources = System.Windows.Application.Current.Resources;
        resources["JujuBackground"] = theme == EffectiveTheme.Dark ? "#202020" : "#F5F5F5";
        resources["JujuForeground"] = theme == EffectiveTheme.Dark ? "#FFFFFF" : "#202020";
        Changed?.Invoke(theme);
    }

    private static EffectiveTheme GetSystemTheme()
    {
        try { return Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize")?.GetValue("AppsUseLightTheme") is 0 ? EffectiveTheme.Dark : EffectiveTheme.Light; }
        catch { return EffectiveTheme.Light; }
    }
}
