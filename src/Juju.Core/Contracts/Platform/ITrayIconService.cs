namespace Juju.Core.Contracts.Platform;

public enum TrayCommand
{
    ShowLauncher,
    ShowSettings,
    Help,
    CheckForUpdates,
    Exit
}

/// <summary>桌面平台托盘入口；菜单文本与原生绘制归平台实现。</summary>
public interface ITrayIconService : IDisposable
{
    event Action<TrayCommand>? CommandRequested;
}
