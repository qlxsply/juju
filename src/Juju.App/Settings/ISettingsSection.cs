using System.Windows;

namespace Juju.App.Settings;

public interface ISettingsSection
{
    string Id { get; }
    string DisplayName { get; }
    FrameworkElement View { get; }
    Task SaveAsync();
}