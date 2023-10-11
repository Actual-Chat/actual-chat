using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ActualChat.Search;
using Cysharp.Text;

namespace ActualChat;

public static partial class StringExt
{
    [GeneratedRegex("([0-9a-z][A-Z])|([a-z][0-9])|([A-Z][0-9])", RegexOptions.ExplicitCapture)]
    private static partial Regex CaseChangeRegexFactory();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelCaseRegexFactory();

    [GeneratedRegex(@"([^\r\n]*)(?:\r?\n)", RegexOptions.ExplicitCapture)]
    private static partial Regex NewLineRegexFactory();

    private static readonly Regex CaseChangeRegex = CaseChangeRegexFactory();
#pragma warning disable MA0023
    private static readonly Regex CamelCaseRegex = CamelCaseRegexFactory();
    private static readonly Regex NewLineRegex = NewLineRegexFactory();
#pragma warning restore MA0023

    public static string RequireNonEmpty(this string? source, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.NullIfEmpty() ?? throw StandardError.Constraint($"{name} is required here.");
    [return: NotNullIfNotNull(nameof(source))]
    public static string? RequireNotEqual(this string? source, string target, [CallerArgumentExpression(nameof(source))] string name = "")
        => OrdinalEquals(source, target)
            ? throw StandardError.Constraint($"{name} should not be {target}.")
            : source;
    [return: NotNullIfNotNull(nameof(source))]
    public static string? RequireEmpty(this string? source, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.IsNullOrEmpty() ? source : throw StandardError.Constraint($"{name} must be null or empty here.");
    public static string? RequireMaxLength(this string source, int length, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.Length <= length ? source : throw StandardError.Constraint($"{name} Must be no more than {length} characters.");

    public static SearchPhrase ToSearchPhrase(this string text, bool matchPrefixes, bool matchSuffixes)
        => new(text, matchPrefixes, matchSuffixes);

    public static string ToSentenceCase(this string str, string delimiter = " ")
        => CaseChangeRegex.Replace(str, m => $"{m.Value[0]}{delimiter}{m.Value[1..]}");

    public static string ToSnakeCase(this string input)
        => input.IsNullOrEmpty()
            ? input
            : CamelCaseRegex.Replace(input, "$1_$2")
                .ToLower(CultureInfo.InvariantCulture)
                .OrdinalReplace("__", "_");

    public static string Capitalize(this string source)
        => source.IsNullOrEmpty() ? source : source.Capitalize(0);

    public static string Capitalize(this string source, int position)
    {
        var firstLetter = source[position];
        var firstLetterUpper = char.ToUpperInvariant(firstLetter);
        if (firstLetter == firstLetterUpper)
            return source;

        using var sb = ZString.CreateStringBuilder(true);
        if (position > 0)
            sb.Append(source.AsSpan(0, position));
        sb.Append(firstLetterUpper);
        sb.Append(source.AsSpan(position + 1));
        return sb.ToString();
    }

    public static string Pluralize(this string source, int count)
        => count == 1 ? source : source + "s";

    public static string EnsureSuffix(this string source, string suffix)
        => source.OrdinalEndsWith(suffix) ? source : source + suffix;

    public static string Truncate(this string source, int maxLength)
        => source.Length <= maxLength ? source : source[..maxLength];
    public static string Truncate(this string source, int maxLength, string ellipsis)
        => source.Length <= maxLength ? source : source[..maxLength] + ellipsis;

    public static int GetIndentLength(this string source)
    {
        for (var i = 0; i < source.Length; i++)
            if (source[i] != 32)
                return i;
        return source.Length;
    }

    public static int GetCommonPrefixLength(this string a, string b)
    {
        for (var i = 0; i < a.Length; i++) {
            if (i >= b.Length)
                return i;
            if (a[i] != b[i])
                return i;
        }
        return a.Length;
    }

    public static IEnumerable<(string Line, bool EndsWithLineFeed)> ParseLines(this string text)
    {
        for (var index = 0; index < text.Length;) {
            var match = NewLineRegex.Match(text, index);
            if (match.Success)
                yield return (match.Groups[1].Value, true);
            else {
                yield return (text[index..], false);
                yield break;
            }
            index = match.Index + match.Length;
        }
    }

    [return: NotNullIfNotNull("url")]
    public static Uri? ToUri(this string? url)
        => url == null ? null : new Uri(url);

    public static string UrlEncode(this string input)
        => WebUtility.UrlEncode(input);
    public static string UrlEncode(this Symbol input)
        => WebUtility.UrlEncode(input);

    public static string UrlDecode(this string input)
        => WebUtility.UrlDecode(input);

    public static string HtmlEncode(this string input)
        => HtmlEncoder.Default.Encode(input);
    public static string HtmlEncode(this Symbol input)
        => HtmlEncoder.Default.Encode(input);

    public static string HtmlDecode(this string input)
        => WebUtility.HtmlDecode(input);
    public static string HtmlDecode(this Symbol input)
        => WebUtility.HtmlDecode(input);

    // ParseXxx

    public static (string Host, ushort Port) ParseHostPort(this string hostPort, ushort defaultPort)
    {
        var (host, port) = hostPort.ParseHostPort();
        port ??= defaultPort;
        return (host, port.GetValueOrDefault());
    }

    public static (string Host, ushort? Port) ParseHostPort(this string hostPort)
    {
        if (!hostPort.TryParseHostPort(out var host, out var port))
            throw new ArgumentOutOfRangeException(nameof(hostPort),
                "Input string should have 'host[:port]' format.");
        return (host, port);
    }

    public static bool TryParseHostPort(
        this string hostPort,
        out string host,
        out ushort? port)
    {
        host = "";
        port = null;
        if (hostPort.IsNullOrEmpty())
            return false;

        var columnIndex = hostPort.OrdinalIndexOf(":");
        if (columnIndex <= 0) {
            host = hostPort;
            return true;
        }

        host = hostPort[..columnIndex];
        var portStr = hostPort[(columnIndex + 1)..];
        if (portStr.IsNullOrEmpty())
            return true;

        if (!ushort.TryParse(portStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var portValue))
            return false;

        port = portValue;
        return true;
    }

    public static string ToBase64(this string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        return Convert.ToBase64String(bytes);
    }

    public static string FromBase64(this string s)
    {
        var bytes = Convert.FromBase64String(s);
        return Encoding.UTF8.GetString(bytes);
    }
}
