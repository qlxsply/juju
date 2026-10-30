namespace Juju.Tools.Toto.Application.Settings;

public interface ITotoSettingsApplicationService
{
    IReadOnlyDictionary<string, string> Settings { get; }
    string GlobalHotkey { get; }
    event Action<string>? HotkeyChanged;
    event Func<string, bool>? HotkeyChanging;
    void SaveSettings(IReadOnlyDictionary<string, string> settings);
}
