using System.Globalization;

namespace MegaCopier.Engine.Updates;

/// <summary>SemVer sin conversiones a decimal ni comparaciones alfabéticas de números.</summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease = "") : IComparable<ReleaseVersion>
{
    public static ReleaseVersion? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return null;
        value = value.Trim();
        if (value.StartsWith('v')) value = value[1..];
        var metadata = value.Split('+');
        if (metadata.Length > 2 || metadata.Length == 2 && metadata[1].Split('.').Any(p => p.Length == 0
            || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))) return null;
        value = metadata[0];
        string[] parts = value.Split('-', 2);
        string[] numbers = parts[0].Split('.');
        if (numbers.Length != 3 || numbers.Any(n => n.Length == 0 || n.Length > 1 && n[0] == '0'
            || !n.All(char.IsAsciiDigit))) return null;
        if (!int.TryParse(numbers[0], out int major) || !int.TryParse(numbers[1], out int minor)
            || !int.TryParse(numbers[2], out int patch)) return null;
        string pre = parts.Length == 2 ? parts[1] : "";
        if (parts.Length == 2 && (pre.Length == 0 || pre.Split('.').Any(p => p.Length == 0
            || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || p.All(char.IsAsciiDigit) && p.Length > 1 && p[0] == '0'))) return null;
        return new(major, minor, patch, pre);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (Prerelease.Length == 0 || other.Prerelease.Length == 0)
            return (Prerelease.Length == 0 ? 1 : 0).CompareTo(other.Prerelease.Length == 0 ? 1 : 0);
        string[] a = Prerelease.Split('.'), b = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            result = an && bn ? a[i].Length.CompareTo(b[i].Length) : an != bn ? (an ? -1 : 1) : 0;
            if (result == 0) result = string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}{(Prerelease.Length == 0 ? "" : "-" + Prerelease)}");
}
