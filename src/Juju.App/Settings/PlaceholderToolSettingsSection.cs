using System.Windows;
using Juju.App.Views;
using Juju.Core.Settings;
using Juju.Core.Storage;
using Juju.Core.Tools;
using Juju.Tools.Json.Settings;
using Toto.App;

namespace Juju.App.Settings;

/// <summary>占位工具也遵循统一的单键、直达热键与工具数据目录规则。</summary>
public sealed class PlaceholderToolSettingsSection : ISettingsSection
{
    private readonly ToolId _tool;
    private readonly ISettingsService _settings;
    private readonly DataDirectoryChangeService _changes;
    private readonly Func<ToolId, string, bool> _tryRegister;
    private readonly SettingsDialogService _dialogs;
    private readonly IJsonToolSettingsService _json;
    private readonly TotoToolHost _toto;
    private readonly Action<string> _renderKey;
    private readonly Action<string> _renderHotkey;
    private readonly Action<string> _renderDirectory;
    public string Id => _tool == ToolId.EchoHttp ? "echo-http" : "kairos";
    public string DisplayName => _tool.ToString();
    public FrameworkElement View { get; }

    public PlaceholderToolSettingsSection(ToolId tool, ISettingsService settings,
        DataDirectoryChangeService changes, Func<ToolId, string, bool> tryRegister,
        SettingsDialogService dialogs, IJsonToolSettingsService json, TotoToolHost toto)
    {
        _tool = tool;
        _settings = settings;
        _changes = changes;
        _tryRegister = tryRegister;
        _dialogs = dialogs;
        _json = json;
        _toto = toto;
        var page = SettingsViewFactory.Page();
        var entryRows = SettingsViewFactory.ShortcutGroup(page, tool + " · 入口",
        [
            ("key", "工具列表单键", settings.Current.ToolKeys[tool.ToString()], (Action)EditKey),
            ("hotkey", "全局直达热键",
                settings.Current.ToolGlobalShortcuts.GetValueOrDefault(tool.ToString(), GlobalHotkeyPolicy.None),
                (Action)EditHotkey)
        ]);
        _renderKey = entryRows["key"];
        _renderHotkey = entryRows["hotkey"];
        var storage = SettingsViewFactory.Group(page, "存储目录");
        _renderDirectory = SettingsViewFactory.DirectoryRow(storage, "工具数据目录",
            changes.DisplayToolDirectory(tool.ToString().ToLowerInvariant()), EditDirectory,
            () => SettingsViewFactory.OpenDirectory(_changes.DisplayToolDirectory(_tool.ToString().ToLowerInvariant())));
        View = page;
    }

    private void EditKey()
    {
        _dialogs.EditShortcut("编辑快捷键 · " + _tool + " 工具列表单键", _settings.Current.ToolKeys[_tool.ToString()],
            false, true, false, ValidateKey, SaveKeyAsync, ToolLaunchKeys.Defaults()[_tool.ToString()]);
    }

    private string? ValidateKey(string candidate)
    {
        try
        {
            var keys = _settings.Current.ToolKeys.ToDictionary(item => item.Key, item => item.Value);
            keys[_tool.ToString()] = candidate;
            ToolLaunchKeys.Normalize(keys);
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private async Task SaveKeyAsync(string candidate)
    {
        var old = _settings.Current;
        var keys = old.ToolKeys.ToDictionary(item => item.Key, item => item.Value);
        keys[_tool.ToString()] = candidate;
        var next = old with { ToolKeys = ToolLaunchKeys.Normalize(keys) };
        var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
        if (launcher is not null && !launcher.ApplySettings(next))
        {
            launcher.ApplySettings(old);
            throw new InvalidOperationException("单键与其他入口按键冲突，原设置保持不变。");
        }
        try
        {
            await _settings.SaveAsync(next);
            _renderKey(candidate);
        }
        catch
        {
            launcher?.ApplySettings(old);
            await _settings.SaveAsync(old);
            throw;
        }
    }

    private void EditHotkey() => _dialogs.EditShortcut("编辑快捷键 · " + _tool + " 全局直达",
        _settings.Current.ToolGlobalShortcuts.GetValueOrDefault(_tool.ToString(), GlobalHotkeyPolicy.None), true, false, false,
        candidate => ValidateHotkey(candidate), SaveHotkeyAsync, GlobalHotkeyPolicy.None);

    private string? ValidateHotkey(string candidate)
    {
        try
        {
            var all = _settings.Current.ToolGlobalShortcuts.ToDictionary(item => item.Key, item => item.Value);
            all[_tool.ToString()] = candidate;
            GlobalHotkeyPolicy.EnsureDistinct(_settings.Current.LeaderShortcut,
                new[] { all[_tool.ToString()], _json.Current.GlobalShortcut, _toto.Hotkey }
                    .Concat(all.Where(item => !item.Key.Equals(_tool.ToString(), StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Value)).ToArray());
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private async Task SaveHotkeyAsync(string candidate)
    {
        if (!_tryRegister(_tool, candidate)) throw new InvalidOperationException("全局直达快捷键已被占用。");
        var old = _settings.Current;
        var all = old.ToolGlobalShortcuts.ToDictionary(item => item.Key, item => item.Value);
        all[_tool.ToString()] = candidate;
        try { await _settings.SaveAsync(old with { ToolGlobalShortcuts = all }); }
        catch
        {
            _tryRegister(_tool, old.ToolGlobalShortcuts.GetValueOrDefault(_tool.ToString(), GlobalHotkeyPolicy.None));
            throw;
        }
        _renderHotkey(candidate);
    }

    private void EditDirectory() => _dialogs.EditFolder("选择 " + _tool + " 数据目录",
        _changes.DisplayToolDirectory(_tool.ToString().ToLowerInvariant()), async path =>
        {
            var old = _settings.Current;
            var name = _tool.ToString().ToLowerInvariant();
            _changes.StageTool(name, path);
            try
            {
                await _settings.SaveAsync(old with
                {
                    PendingToolDataDirectories = new Dictionary<string, string>(_changes.PendingTools,
                        StringComparer.OrdinalIgnoreCase)
                });
                _renderDirectory(_changes.DisplayToolDirectory(name));
            }
            catch
            {
                _changes.LoadPending(old.PendingDataRoot, old.PendingToolDataDirectories);
                throw;
            }
        });

    public Task SaveAsync() => Task.CompletedTask;
}
