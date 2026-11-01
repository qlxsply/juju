using System.Windows;

namespace Juju.App.Contracts.Windows;

/// <summary>仅协调桌面通用窗口；具体工具窗口由工具适配器管理。</summary>
public interface IAppWindowManager
{
    void ShowLauncher();
    void ShowSettings();
    void ShowUpdateSettings();
    void PrepareToolWindowOpen();
    void CloseAllTools();
    Task<bool> PrepareShutdownAsync();
    void CancelShutdownPreparation();
}

public interface IJsonWindowFactory
{
    Window? MainWindow { get; }
    void ShowJson();
    void Close();
    Task<bool> PrepareShutdownAsync();
    void CancelShutdownPreparation();
    Task<bool> ShutdownAsync();
    event Action? Hidden;
}

public interface ITotoWindowFactory
{
    Window? MainWindow { get; }
    void ShowToto();
    void ShowHistory();
    void RefreshMain();
    void CloseForShutdown();
    event Action? Hidden;
}
