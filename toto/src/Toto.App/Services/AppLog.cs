namespace Toto.App.Services;

internal sealed class AppLog(string directory)
{
    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"toto-{DateTime.Now:yyyyMM}.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignored
        }
    }
}