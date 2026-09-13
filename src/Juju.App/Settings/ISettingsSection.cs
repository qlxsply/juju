using System.Windows;

namespace Juju.App.Settings;

// 设置窗口依赖此小接口而非具体页面，DI 可以注入多个实现并按 Id 分组展示。
public interface ISettingsSection
{
    string Id { get; }
    string DisplayName { get; }

    FrameworkElement View { get; }

    // 异步保存允许文件 I/O 不阻塞 WPF Dispatcher/UI 线程。
    Task SaveAsync();
}