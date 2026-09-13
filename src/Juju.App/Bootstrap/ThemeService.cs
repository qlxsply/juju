using Microsoft.Win32;
using System.Windows;
using Juju.Core.Settings;

namespace Juju.App.Bootstrap;

// EffectiveTheme 是已解析的最终主题；它与“跟随系统”的用户偏好分开，避免下游重复读取注册表。
public enum EffectiveTheme
{
    Light,
    Dark
}

// 单例主题服务通过 WPF Application.Resources 传播画刷，并用 Changed 通知已创建的窗口。
public sealed class ThemeService
{
    public event Action<EffectiveTheme>? Changed;
    public EffectiveTheme Current { get; private set; } = EffectiveTheme.Light;

    // WPF 动态资源会从 Application 资源字典重新解析；事件使 WebView 等非 XAML 消费者同步主题。
    public void Apply(ThemePreference preference)
    {
        var theme = preference == ThemePreference.System ? GetSystemTheme() :
            preference == ThemePreference.Dark ? EffectiveTheme.Dark : EffectiveTheme.Light;
        Current = theme;
        var resources = System.Windows.Application.Current.Resources;
        resources["JujuBackground"] = theme == EffectiveTheme.Dark
            ? System.Windows.Media.Brushes.Black
            : System.Windows.Media.Brushes.WhiteSmoke;
        resources["JujuForeground"] = theme == EffectiveTheme.Dark
            ? System.Windows.Media.Brushes.White
            : System.Windows.Media.Brushes.Black;
        Changed?.Invoke(theme);
    }

    // Windows 将应用浅色偏好存于当前用户注册表；读取失败时保守回退到浅色主题。
    private static EffectiveTheme GetSystemTheme()
    {
        try
        {
            return Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")
                ?.GetValue("AppsUseLightTheme") is 0
                ? EffectiveTheme.Dark
                : EffectiveTheme.Light;
        }
        catch
        {
            return EffectiveTheme.Light;
        }
    }
}