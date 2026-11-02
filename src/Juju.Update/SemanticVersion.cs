using System.Globalization;
using System.Text.RegularExpressions;

namespace Juju.Update;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"^(?:juju_v)?(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if ( value is null )
            return false;
        var match = Pattern.Match(value);
        if ( !match.Success ||
             !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
             !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
             !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch) )
            return false;
        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if ( major != 0 )
            return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
}
