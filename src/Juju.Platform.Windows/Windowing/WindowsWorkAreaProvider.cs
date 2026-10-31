using Juju.Core.Contracts.Platform;
using System.Drawing;
using System.Windows.Forms;

namespace Juju.Platform.Windows.Windowing;

public sealed class WindowsWorkAreaProvider : IWorkAreaProvider
{
    public WorkAreaBounds GetWorkArea(double x, double y)
    {
        var bounds = Screen.FromPoint(new Point((int)x, (int)y)).WorkingArea;
        return new WorkAreaBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    public IReadOnlyList<WorkAreaBounds> GetWorkAreas() => Screen.AllScreens
        .Select(screen => screen.WorkingArea)
        .Select(bounds => new WorkAreaBounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom))
        .ToArray();
}
