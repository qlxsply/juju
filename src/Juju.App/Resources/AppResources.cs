using System.Windows;

namespace Juju.App.Resources;

/// <summary>读取 App.xaml 中必需的共享资源，并在缺失或类型错误时给出明确错误。</summary>
internal static class AppResources
{
    public static T GetRequired<T>(string key) where T : class
    {
        if ( Application.Current?.TryFindResource(key) is T resource )
            return resource;

        throw new InvalidOperationException(
            $"Required application resource '{key}' is missing or is not a {typeof(T).Name}.");
    }
}
