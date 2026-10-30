namespace Juju.App.Contracts.Windows;

/// <summary>仅协调桌面通用窗口；具体工具窗口由工具适配器管理。</summary>
public interface IAppWindowManager
{
    void ShowLauncher();
    void ShowSettings();
    void PrepareToolWindowOpen();
    void CloseAllTools();
}

public interface IJsonToolWindow
{
    void ShowJson();
}

public interface ITotoToolWindow
{
    void ShowToto();
}
