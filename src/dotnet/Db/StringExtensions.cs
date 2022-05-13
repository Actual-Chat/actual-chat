using System.Text.RegularExpressions;

namespace ActualChat.Db;

internal static class StringExtensions
{
    private static readonly Regex _camelCaseRegex = new (@"([a-z0-9])([A-Z])", RegexOptions.Compiled);

    public static string ToSnakeCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return _camelCaseRegex.Replace(input, "$1_$2")
            .ToLower(CultureInfo.InvariantCulture)
            .Replace("__", "_", StringComparison.InvariantCulture);
    }
}
