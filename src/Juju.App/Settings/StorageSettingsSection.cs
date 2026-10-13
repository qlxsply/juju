using System.Diagnostics;
using System.IO;
using System.Windows;
using Juju.Core.Storage;

namespace Juju.App.Settings;

/// <summary>展示应用的真实数据位置；不提供原型中尚未实现的自动备份或保留策略。</summary>
public sealed class StorageSettingsSection : ISettingsSection
{
    public string Id => "storage";
    public string DisplayName => "存储设置";
    public FrameworkElement View { get; }

    public StorageSettingsSection()
    {
        var page = SettingsViewFactory.Page("APPLICATION / STORAGE", "存储设置",
            "应用数据保存在安装目录下，JSON 与 Toto 各自使用独立目录。");
        var card = SettingsViewFactory.Group(page, "数据目录");
        SettingsViewFactory.Row(card, "应用设置", ApplicationPaths.ConfigurationFile,
            SettingsViewFactory.SoftButton("打开目录", (_, _) => Open(ApplicationPaths.DataRoot)));
        SettingsViewFactory.Row(card, "JSON 文档与工具配置", ApplicationPaths.ToolDataDirectory("json"),
            SettingsViewFactory.SoftButton("打开 JSON 目录", (_, _) => Open(ApplicationPaths.ToolDataDirectory("json"))));
        SettingsViewFactory.Row(card, "Toto 事项、日历和日志", ApplicationPaths.ToolDataDirectory("toto"),
            SettingsViewFactory.SoftButton("打开 Toto 目录", (_, _) => Open(ApplicationPaths.ToolDataDirectory("toto"))));
        SettingsViewFactory.Note(page, "旧版 Toto 用户目录仅在首次迁移时复制缺失文件，原文件不会被删除。");
        View = page;
    }

    private static void Open(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    public Task SaveAsync() => Task.CompletedTask;
}
