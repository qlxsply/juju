using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Juju.Tools.Json.Settings;
using Juju.Core.Settings;
using Juju.Core.Tools;
using Juju.App.Views;
using Juju.Core.Storage;
using Juju.Tools.Toto.Application;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace Juju.App.Settings;

// JSON 设置页以运行时创建的 WPF 控件实现，并通过 ISettingsSection 接口被 SettingsWindow 发现。
public sealed class JsonSettingsSection : ISettingsSection
{
    private readonly IJsonToolSettingsService _settings;
    private readonly ISettingsService _applicationSettings;
    private readonly DataDirectoryChangeService _dataChanges;
    private readonly Func<ToolId, string, bool> _tryRegisterHotkey;
    private readonly SettingsDialogService _dialogs;
    private readonly ITotoApplicationService _toto;
    private readonly Action<string> _renderLaunchKey;
    private readonly Action<string> _renderGlobalShortcut;
    private readonly Action<string> _renderDirectory;
    private readonly System.Windows.Controls.TextBox _autosave = new();
    private readonly System.Windows.Controls.TextBox _indent = new();
    private readonly System.Windows.Controls.CheckBox _keep;
    private readonly System.Windows.Controls.CheckBox _maximized;
    private readonly System.Windows.Controls.CheckBox _remember;

    private readonly List<ShortcutRow> _rows;
    private readonly Dictionary<JsonToolCommand, Action<string>> _renderShortcutRows = [];

    public string Id => "json";
    public string DisplayName => "JSON";
    public string NavigationName => "JSON 格式化";
    public FrameworkElement View { get; }

    // 设置服务由 DI 注入；控件只编辑内存中的行模型，点击总保存时才写入持久化配置。
    public JsonSettingsSection(IJsonToolSettingsService settings, ISettingsService applicationSettings,
        DataDirectoryChangeService dataChanges,
        Func<ToolId, string, bool> tryRegisterHotkey, SettingsDialogService dialogs, ITotoApplicationService toto)
    {
        _settings = settings;
        _applicationSettings = applicationSettings;
        _dataChanges = dataChanges;
        _tryRegisterHotkey = tryRegisterHotkey;
        _dialogs = dialogs;
        _toto = toto;
        var panel = SettingsViewFactory.Page();

        var entryRows = SettingsViewFactory.ShortcutGroup(panel, "工具入口",
        [
            ("launcher", "主界面快捷键", applicationSettings.Current.ToolKeys[nameof(ToolId.Json)], (Action)EditLaunchKey),
            ("global", "全局快捷键", settings.Current.GlobalShortcut, (Action)EditGlobalShortcut)
        ]);
        _renderLaunchKey = entryRows["launcher"];
        _renderGlobalShortcut = entryRows["global"];

        var behavior = SettingsViewFactory.Group(panel, "窗口与进程");
        _keep = SettingsViewFactory.Switch("关闭时保留窗口会话", settings.Current.KeepWindowOnClose);
        SettingsViewFactory.Row(behavior, "关闭窗口后保留会话", "关闭后隐藏；再次打开会复用同一个窗口", _keep);
        _maximized = SettingsViewFactory.Switch("默认最大化打开工具", settings.Current.OpenMaximized);
        SettingsViewFactory.Row(behavior, "默认最大化打开", "仅控制工具下次打开时的默认尺寸", _maximized);
        _remember = SettingsViewFactory.Switch("记住工具窗口大小和位置", settings.Current.RememberWindowPlacement);
        SettingsViewFactory.Row(behavior, "记住窗口大小与位置", "退出工具时记录窗口最近的位置与尺寸", _remember);

        var options = SettingsViewFactory.Group(panel, "编辑设置");
        _autosave = SettingsViewFactory.Input(settings.Current.AutosaveDelayMilliseconds.ToString(), 95);
        SettingsViewFactory.Row(options, "自动保存延迟（毫秒）", "编辑器停止输入后等待写入磁盘的时间", _autosave);
        _indent = SettingsViewFactory.Input(settings.Current.IndentSize.ToString(), 95);
        SettingsViewFactory.Row(options, "缩进空格数", "格式化时的缩进宽度", _indent);
        var storage = SettingsViewFactory.Group(panel, "存储目录");
        _renderDirectory = SettingsViewFactory.DirectoryRow(storage, "数据目录",
            dataChanges.DisplayToolDirectory("json"), EditDirectory,
            () => SettingsViewFactory.OpenDirectory(_dataChanges.DisplayToolDirectory("json")));

        _rows = JsonToolShortcuts.Definitions
            .Select(definition => new ShortcutRow(definition, settings.Current.Shortcuts[definition.Command])).ToList();
        var basicRows = _rows.Where(row => row.SourceKind == JsonToolShortcutSource.Juju &&
                                            row.Command != JsonToolCommand.UnfoldLevel).ToArray();
        var basicRenderers = SettingsViewFactory.ShortcutGroup(panel, "基础快捷键",
            basicRows.Select(row => (row.Command.ToString(), row.CommandName, row.Shortcut,
                (Action)(() => EditEditorShortcut(row)))));
        foreach (var row in basicRows) _renderShortcutRows[row.Command] = basicRenderers[row.Command.ToString()];
        var editorRows = _rows.Where(row => row.SourceKind == JsonToolShortcutSource.Monaco ||
                                             row.Command == JsonToolCommand.UnfoldLevel).ToArray();
        var editorRenderers = SettingsViewFactory.ShortcutGroup(panel, "编辑器快捷键",
            editorRows.Select(row => (row.Command.ToString(), row.CommandName, row.Shortcut,
                (Action)(() => EditEditorShortcut(row)))));
        foreach (var row in editorRows) _renderShortcutRows[row.Command] = editorRenderers[row.Command.ToString()];
        View = panel;
    }

    // 目标设置是 record 风格的不可变值；对象初始化器仅替换 Shortcuts 属性。
    public async Task SaveAsync()
    {
        var previous = _settings.Current;
        var next = new JsonToolSettings(ReadNumber(_autosave, 100, 10000), ReadNumber(_indent, 1, 8),
            _keep.IsChecked == true, _maximized.IsChecked == true, _remember.IsChecked == true)
        {
            GlobalShortcut = previous.GlobalShortcut,
            Shortcuts = previous.Shortcuts
        };
        JsonToolShortcuts.Validate(next.Shortcuts);
        try
        {
            await _settings.SaveAsync(next);
        }
        catch
        {
            await _settings.SaveAsync(previous);
            throw;
        }
    }

    private void EditLaunchKey()
    {
        var current = _applicationSettings.Current.ToolKeys[nameof(ToolId.Json)];
        _dialogs.EditShortcut("编辑快捷键 · JSON 工具列表单键", current, false, true, false,
            candidate => CandidateToolKeyError(ToolId.Json, candidate), async candidate =>
            {
                var old = _applicationSettings.Current;
                var values = old.ToolKeys.ToDictionary(pair => pair.Key, pair => pair.Value);
                values[nameof(ToolId.Json)] = candidate;
                var next = old with { ToolKeys = ToolLaunchKeys.Normalize(values) };
                var launcher = System.Windows.Application.Current.Windows.OfType<LauncherWindow>().FirstOrDefault();
                if (launcher is not null && !launcher.ApplySettings(next))
                {
                    launcher.ApplySettings(old);
                    throw new InvalidOperationException("单键与其他入口按键冲突，原设置保持不变。");
                }
                try
                {
                    await _applicationSettings.SaveAsync(next);
                    _renderLaunchKey(candidate);
                }
                catch
                {
                    launcher?.ApplySettings(old);
                    await _applicationSettings.SaveAsync(old);
                    throw;
                }
            }, ToolLaunchKeys.Json.ToString());
    }

    private void EditGlobalShortcut()
    {
        var current = _settings.Current.GlobalShortcut;
        _dialogs.EditShortcut("编辑快捷键 · JSON 全局直达", current, true, false, false,
            candidate =>
            {
                try
                {
                    var normalized = GlobalHotkeyPolicy.Normalize(candidate, optional: true);
                    GlobalHotkeyPolicy.EnsureDistinct(_applicationSettings.Current.LeaderShortcut,
                        new[] { normalized, _toto.GlobalHotkey }.Concat(_applicationSettings.Current.ToolGlobalShortcuts.Values).ToArray());
                    return null;
                }
                catch (InvalidOperationException ex) { return ex.Message; }
            }, SaveGlobalShortcutAsync, defaultValue: GlobalHotkeyPolicy.None);
    }

    private async Task SaveGlobalShortcutAsync(string shortcut)
    {
        var previous = _settings.Current;
        if (!_tryRegisterHotkey(ToolId.Json, shortcut)) throw new InvalidOperationException("JSON 全局热键无效或已占用。");
        try
        {
            await _settings.SaveAsync(previous with { GlobalShortcut = GlobalHotkeyPolicy.Normalize(shortcut, true) });
            _renderGlobalShortcut(_settings.Current.GlobalShortcut);
        }
        catch
        {
            _tryRegisterHotkey(ToolId.Json, previous.GlobalShortcut);
            throw;
        }
    }

    private string? CandidateToolKeyError(ToolId tool, string candidate)
    {
        try
        {
            var values = _applicationSettings.Current.ToolKeys.ToDictionary(pair => pair.Key, pair => pair.Value);
            values[tool.ToString()] = candidate;
            ToolLaunchKeys.Normalize(values);
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private void EditDirectory() => _dialogs.EditFolder("选择 JSON 数据目录", _dataChanges.DisplayToolDirectory("json"),
        async path =>
        {
            var old = _applicationSettings.Current;
            _dataChanges.StageTool("json", path);
            try
            {
                await _applicationSettings.SaveAsync(old with
                {
                    PendingToolDataDirectories = new Dictionary<string, string>(_dataChanges.PendingTools,
                        StringComparer.OrdinalIgnoreCase)
                });
                _renderDirectory(_dataChanges.DisplayToolDirectory("json"));
            }
            catch
            {
                _dataChanges.LoadPending(old.PendingDataRoot, old.PendingToolDataDirectories);
                throw;
            }
        });

    public async Task SaveImmediateAsync(FrameworkElement source)
    {
        var previous = _settings.Current;
        try
        {
            JsonToolSettings? next = null;
            if (ReferenceEquals(source, _keep)) next = previous with { KeepWindowOnClose = _keep.IsChecked == true };
            else if (ReferenceEquals(source, _maximized)) next = previous with { OpenMaximized = _maximized.IsChecked == true };
            else if (ReferenceEquals(source, _remember)) next = previous with { RememberWindowPlacement = _remember.IsChecked == true };
            else if (ReferenceEquals(source, _autosave)) next = previous with { AutosaveDelayMilliseconds = ReadNumber(_autosave, 100, 10000) };
            else if (ReferenceEquals(source, _indent)) next = previous with { IndentSize = ReadNumber(_indent, 1, 8) };
            else return; // 快捷键、路径和入口单键由专用弹窗提交，不能保存半成品。
            await _settings.SaveAsync(next);
            _autosave.Text = next.AutosaveDelayMilliseconds.ToString();
            _indent.Text = next.IndentSize.ToString();
        }
        catch
        {
            _autosave.Text = previous.AutosaveDelayMilliseconds.ToString();
            _indent.Text = previous.IndentSize.ToString();
            _keep.IsChecked = previous.KeepWindowOnClose;
            _maximized.IsChecked = previous.OpenMaximized;
            _remember.IsChecked = previous.RememberWindowPlacement;
            throw;
        }
    }

    // 数据模型行来自已保存快照；点击铅笔后弹窗编辑副本，确认成功才替换行并落盘。
    private void EditEditorShortcut(ShortcutRow row)
    {
        _dialogs.EditShortcut("编辑快捷键 · " + row.CommandName, row.Shortcut, allowNone: false,
            allowSingle: false, allowSequence: row.Default.Contains(' '),
            validate: candidate =>
            {
                try
                {
                    var values = _settings.Current.Shortcuts.ToDictionary(pair => pair.Key, pair => pair.Value);
                    values[row.Command] = candidate;
                    JsonToolShortcuts.Validate(JsonToolShortcuts.Normalize(values));
                    return null;
                }
                catch (InvalidOperationException ex) { return ex.Message; }
            },
            commit: async candidate =>
            {
                var values = _settings.Current.Shortcuts.ToDictionary(pair => pair.Key, pair => pair.Value);
                values[row.Command] = candidate;
                await _settings.SaveAsync(_settings.Current with { Shortcuts = values });
                row.Shortcut = _settings.Current.Shortcuts[row.Command];
                _renderShortcutRows[row.Command](row.Shortcut);
            }, defaultValue: row.Default, allowUnmodified: true);
    }

    private static int ReadNumber(System.Windows.Controls.TextBox box, int minimum, int maximum) =>
        int.TryParse(box.Text, out var value) && value >= minimum && value <= maximum
            ? value
            : throw new InvalidOperationException($"{box.Text} 不是有效范围内的数字。");

    // C# 主构造函数把只用于初始化的参数放在类型声明处；Java 中可理解为简化的构造函数和字段赋值。
    private sealed class ShortcutRow(JsonToolCommandMetadata definition, string shortcut)
    {
        public string Shortcut { get; set; } = shortcut;
        public JsonToolCommand Command => definition.Command;
        public string CommandName => definition.DisplayName;
        public JsonToolShortcutSource SourceKind => definition.Source;

        public string Default => definition.Default;

    }
}
