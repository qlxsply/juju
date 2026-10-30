using Juju.App.Tools.Contracts;
using MessageBox = System.Windows.MessageBox;

namespace Juju.App.Bootstrap;

public sealed class ToolOpenFeedback : IToolOpenFeedback
{
    public void Show(string title, string message, ToolOpenMessageKind kind) =>
        MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK,
            kind == ToolOpenMessageKind.Information
                ? System.Windows.MessageBoxImage.Information
                : System.Windows.MessageBoxImage.Error);
}
