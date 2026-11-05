namespace Juju.Update;

/// <summary>Cross-process exclusive lock shared by installer activation, the launcher and updater.</summary>
public static class InstallRootLock
{
    private const int RetryMilliseconds = 100;

    public static async Task<Lease> AcquireAsync(string installRoot, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeRoot(installRoot);
        var stateDirectory = Path.Combine(root, "state");
        Directory.CreateDirectory(stateDirectory);
        EnsureNotReparsePoint(root, stateDirectory);
        var lockPath = Path.Combine(stateDirectory, "install.lock");
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(2));
        while ( true )
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                EnsureNotReparsePoint(root, lockPath);
                var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.WriteThrough);
                return new Lease(root, stream);
            }
            catch ( IOException ) when ( DateTime.UtcNow < deadline )
            {
                await Task.Delay(RetryMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static string NormalizeRoot(string installRoot)
    {
        if ( string.IsNullOrWhiteSpace(installRoot) )
            throw new ArgumentException("Install root is required.", nameof(installRoot));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
    }

    public static void EnsureNotReparsePoint(string root, string path)
    {
        root = NormalizeRoot(root);
        var current = Path.GetFullPath(path);
        if ( !current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(current, root, StringComparison.OrdinalIgnoreCase) )
            throw new InvalidDataException("Path escapes the juju install root.");

        var relative = Path.GetRelativePath(root, current);
        var cursor = root;
        Check(cursor);
        if ( relative == "." )
            return;
        foreach ( var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) )
        {
            cursor = Path.Combine(cursor, part);
            Check(cursor);
        }

        void Check(string candidate)
        {
            if ( !File.Exists(candidate) && !Directory.Exists(candidate) )
                return;
            if ( (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0 )
                throw new InvalidDataException($"Reparse points are not allowed in installation paths: {candidate}");
        }
    }

    public sealed class Lease : IDisposable
    {
        private FileStream? _stream;

        internal Lease(string root, FileStream stream)
        {
            Root = root;
            _stream = stream;
        }

        public string Root { get; }

        public bool IsFor(string installRoot) =>
            string.Equals(Root, NormalizeRoot(installRoot), StringComparison.OrdinalIgnoreCase);

        public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
    }
}
