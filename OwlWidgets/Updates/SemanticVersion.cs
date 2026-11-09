using System.Globalization;

namespace OwlWidgets.Updates;

/// <summary>
/// A version by SemVer 2.0: <c>major.minor.patch</c>, an optional pre-release after <c>-</c>.
/// Build metadata after <c>+</c> is dropped, as it takes no part in the order.
/// </summary>
internal sealed record SemanticVersion(int Major, int Minor, int Patch, string PreRelease = "") : IComparable<SemanticVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /// <summary>Takes a release tag as well: a leading <c>v</c> is allowed.</summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = new SemanticVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value[0] is 'v' or 'V')
            value = value[1..];
        var metadata = value.IndexOf('+');
        if (metadata >= 0)
            value = value[..metadata];

        var preRelease = "";
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
            if (preRelease.Length == 0 || preRelease.Split('.').Any(part => part.Length == 0))
                return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
            return false;

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a semantic version.");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0)
            result = Minor.CompareTo(other.Minor);
        if (result == 0)
            result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

    private static bool TryParseNumber(string text, out int number) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// A release is above any of its pre-releases. Identifiers are compared one by one:
    /// numbers as numbers and below words, words by ordinal; the longer list wins a tie.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
            return right.Length.CompareTo(left.Length);

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftNumeric = TryParseNumber(leftParts[i], out var leftNumber);
            var rightNumeric = TryParseNumber(rightParts[i], out var rightNumber);
            var result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftParts[i], rightParts[i])
            };
            if (result != 0)
                return result;
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
