namespace Juju.Core.Contracts.Platform;

public readonly record struct WorkAreaBounds(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// <summary>返回包含指定桌面坐标的显示器可用工作区。</summary>
public interface IWorkAreaProvider
{
    WorkAreaBounds GetWorkArea(double x, double y);
    IReadOnlyList<WorkAreaBounds> GetWorkAreas();
}
