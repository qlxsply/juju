using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Juju.App.Views;

/// <summary>Tool-window ControlTemplate metadata and routed window-button behavior shared by tool windows.</summary>
public static class ToolWindowChrome
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ToolWindowChrome), new PropertyMetadata(false, OnIsEnabledChanged));
    public static readonly DependencyProperty StatusLeftProperty = DependencyProperty.RegisterAttached(
        "StatusLeft", typeof(string), typeof(ToolWindowChrome), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StatusCenterProperty = DependencyProperty.RegisterAttached(
        "StatusCenter", typeof(string), typeof(ToolWindowChrome), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StatusRightProperty = DependencyProperty.RegisterAttached(
        "StatusRight", typeof(string), typeof(ToolWindowChrome), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ToolIconProperty = DependencyProperty.RegisterAttached(
        "ToolIcon", typeof(ImageSource), typeof(ToolWindowChrome), new PropertyMetadata(null));
    public static readonly DependencyProperty ShowStatusBarProperty = DependencyProperty.RegisterAttached(
        "ShowStatusBar", typeof(bool), typeof(ToolWindowChrome), new PropertyMetadata(true, OnTemplateOptionChanged));
    public static readonly DependencyProperty ShowMaximizeButtonProperty = DependencyProperty.RegisterAttached(
        "ShowMaximizeButton", typeof(bool), typeof(ToolWindowChrome), new PropertyMetadata(true, OnTemplateOptionChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetStatusLeft(DependencyObject element, string value) => element.SetValue(StatusLeftProperty, value);
    public static string GetStatusLeft(DependencyObject element) => (string)element.GetValue(StatusLeftProperty);
    public static void SetStatusCenter(DependencyObject element, string value) => element.SetValue(StatusCenterProperty, value);
    public static string GetStatusCenter(DependencyObject element) => (string)element.GetValue(StatusCenterProperty);
    public static void SetStatusRight(DependencyObject element, string value) => element.SetValue(StatusRightProperty, value);
    public static string GetStatusRight(DependencyObject element) => (string)element.GetValue(StatusRightProperty);
    public static void SetToolIcon(DependencyObject element, ImageSource value) => element.SetValue(ToolIconProperty, value);
    public static ImageSource? GetToolIcon(DependencyObject element) => (ImageSource?)element.GetValue(ToolIconProperty);
    public static void SetShowStatusBar(DependencyObject element, bool value) => element.SetValue(ShowStatusBarProperty, value);
    public static bool GetShowStatusBar(DependencyObject element) => (bool)element.GetValue(ShowStatusBarProperty);
    public static void SetShowMaximizeButton(DependencyObject element, bool value) => element.SetValue(ShowMaximizeButtonProperty, value);
    public static bool GetShowMaximizeButton(DependencyObject element) => (bool)element.GetValue(ShowMaximizeButtonProperty);

    private static void OnTemplateOptionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not Window window) return;
        if (window.IsLoaded) ApplyTemplateOptions(window);
        else
        {
            RoutedEventHandler? loaded = null;
            loaded = (_, _) =>
            {
                window.Loaded -= loaded;
                ApplyTemplateOptions(window);
            };
            window.Loaded += loaded;
        }
    }

    private static void ApplyTemplateOptions(Window window)
    {
        window.ApplyTemplate();
        if (window.Template.FindName("StatusBar", window) is FrameworkElement statusBar)
            statusBar.Visibility = GetShowStatusBar(window) ? Visibility.Visible : Visibility.Collapsed;
        if (window.Template.FindName("MaximizeButton", window) is FrameworkElement maximizeButton)
            maximizeButton.Visibility = GetShowMaximizeButton(window) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnIsEnabledChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not Window window) return;
        if ((bool)args.NewValue) window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(WindowButton_Click));
        else window.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(WindowButton_Click));
    }

    private static void WindowButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Window window || args.OriginalSource is not Button { Tag: string command }) return;
        switch (command)
        {
            case "Maximize":
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                break;
            case "Close":
                window.Close();
                break;
        }
    }
}
