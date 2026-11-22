using System.Windows;
using Juju.App.Contracts.Windows;
using Juju.App.Views;
using Juju.Tools.MySqlSchema.Application;

namespace Juju.App.Presentation.MySqlSchema;

public sealed class MySqlSchemaWindowFactory(MySqlSchemaWorkspace workspace) : IMySqlSchemaWindowFactory
{
    private MySqlSchemaWindow? _window;

    public void Show()
    {
        if ( _window is null )
        {
            var created = new MySqlSchemaWindow(workspace);
            _window = created;
            created.Closed += (_, _) =>
            {
                if ( ReferenceEquals(_window, created) )
                    _window = null;
            };
        }
        if ( !_window.IsVisible )
            _window.Show();
        if ( _window.WindowState == WindowState.Minimized )
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
