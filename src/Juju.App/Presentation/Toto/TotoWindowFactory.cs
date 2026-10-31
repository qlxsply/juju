using System.Windows;
using Juju.App.Bootstrap;
using Juju.App.Contracts.Windows;
using Juju.App.Views;
using Juju.Tools.Toto.Application;

namespace Juju.App.Presentation.Toto;

/// <summary>独占 Toto 主窗口、历史窗口创建及隐藏后的复用。</summary>
public sealed class TotoWindowFactory : ITotoWindowFactory
{
    private readonly ITotoApplicationService _toto;
    private readonly WindowStateService _windowState;
    private TotoMainWindow? _main;

    public TotoWindowFactory(ITotoApplicationService toto, WindowStateService windowState)
    {
        _toto = toto;
        _windowState = windowState;
    }

    public Window? MainWindow => _main;
    public event Action? Hidden;

    public void ShowToto()
    {
        if (_main is null)
        {
            _main = new TotoMainWindow(_toto, ShowHistory);
            var window = _main;
            if (_windowState.HasPlacement("toto")) _windowState.Restore(window, "toto");
            window.Closed += (_, _) => { if (ReferenceEquals(_main, window)) _main = null; };
            window.IsVisibleChanged += (_, _) =>
            {
                if (!window.IsVisible) Hidden?.Invoke();
            };
        }

        _main.RefreshItems();
        Show(_main);
        _main.FocusItems();
    }

    public void ShowHistory()
    {
        if (_main is { IsVisible: true } owner)
            new TotoHistoryWindow(_toto) { Owner = owner }.ShowDialog();
    }

    public void RefreshMain() => _main?.RefreshItems();

    public void CloseForShutdown()
    {
        if (_main is null) return;
        _main.AllowShutdown();
        _main.Close();
    }

    private static void Show(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
