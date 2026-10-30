using Juju.App.Contracts.Settings;
using Juju.Core.Tools;

namespace Juju.App.Tools.Contracts;

public enum ToolOpenMessageKind
{
    Information,
    Error
}

public sealed record ToolReadiness(bool IsReady, string? Message = null,
    ToolOpenMessageKind MessageKind = ToolOpenMessageKind.Error, string? MessageTitle = null);

/// <summary>Launcher 专属描述，避免展示文案、资源键和占位符状态进入 Core。</summary>
public sealed record LauncherToolMetadata(
    ToolId Id,
    int Order,
    string Name,
    string Description,
    string AccessibilityName,
    string? IconKey,
    string? Glyph,
    string GlyphColor,
    double GlyphFontSize,
    string DefaultLauncherKey,
    bool HideLauncherOnOpen);

public interface IToolCatalog
{
    IReadOnlyList<LauncherToolMetadata> All { get; }
    LauncherToolMetadata Get(ToolId id);
}

/// <summary>提供元数据、就绪检查、打开、设置页贡献及可选后台生命周期的桌面工具模块。</summary>
public interface IToolModule
{
    ToolDescriptor Descriptor { get; }
    LauncherToolMetadata Launcher { get; }
    IReadOnlyCollection<ISettingsSection> SettingsSections { get; }
    ToolReadiness CheckReadiness();
    void Open();
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IToolOpenFeedback
{
    void Show(string title, string message, ToolOpenMessageKind kind);
}

/// <summary>唯一桌面工具路由及后台模块生命周期入口。</summary>
public interface IToolManager
{
    IToolCatalog Catalog { get; }
    IReadOnlyCollection<ISettingsSection> SettingsSections { get; }
    void Open(ToolId tool);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAllAsync(CancellationToken cancellationToken = default);
}
