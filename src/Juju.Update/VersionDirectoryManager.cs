namespace Juju.Update;

public static class VersionDirectoryManager
{
    public static IReadOnlyList<string> Prune(string installRoot, VersionState state)
    {
        var versionsRoot = Path.GetFullPath(Path.Combine(installRoot, "versions"));
        if ( !Directory.Exists(versionsRoot) )
            return [];
        if ( (File.GetAttributes(versionsRoot) & FileAttributes.ReparsePoint) != 0 )
            return [];
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { state.ActiveVersion };
        if ( state.PreviousVersion is not null )
            keep.Add(state.PreviousVersion);
        var removed = new List<string>();
        foreach ( var directory in Directory.EnumerateDirectories(versionsRoot) )
        {
            var version = Path.GetFileName(directory);
            if ( !SemanticVersion.TryParse(version, out _) || keep.Contains(version) )
                continue;
            var full = Path.GetFullPath(directory);
            if ( !full.StartsWith(versionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) )
                continue;
            try
            {
                DeleteTreeWithoutFollowingLinks(full);
                removed.Add(version);
            }
            catch ( IOException ) { }
            catch ( UnauthorizedAccessException ) { }
        }
        return removed;
    }

    public static void DeleteTreeWithoutFollowingLinks(string directory)
    {
        if ( !Directory.Exists(directory) && !File.Exists(directory) )
            return;
        var attributes = File.GetAttributes(directory);
        if ( (attributes & FileAttributes.ReparsePoint) != 0 )
        {
            if ( (attributes & FileAttributes.Directory) != 0 )
                Directory.Delete(directory, recursive: false);
            else
                File.Delete(directory);
            return;
        }

        if ( (attributes & FileAttributes.Directory) == 0 )
        {
            File.Delete(directory);
            return;
        }

        foreach ( var entry in Directory.EnumerateFileSystemEntries(directory) )
            DeleteTreeWithoutFollowingLinks(entry);
        Directory.Delete(directory, recursive: false);
    }
}
