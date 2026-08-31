using Microsoft.Data.Sqlite;

namespace Toto.App.Data;

internal sealed class TotoDatabase(AppPaths paths)
{
    public SqliteConnection Open()
    {
        Directory.CreateDirectory(paths.DataDirectory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath, Pooling = true }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }
}
