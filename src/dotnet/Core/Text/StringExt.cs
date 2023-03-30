using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using ActualChat.Search;
using Cysharp.Text;

namespace ActualChat;

public static class StringExt
{
    private static readonly Regex CaseChangeRegex =
        new("([0-9a-z][A-Z])|([a-z][0-9])|([A-Z][0-9])", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
#pragma warning disable MA0023
    private static readonly Regex CamelCaseRegex = new (@"([a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex NewLineRegex = new(@"([^\r\n]*)(?:\r?\n)", RegexOptions.Compiled);
#pragma warning restore MA0023

    public static string RequireNonEmpty(this string? source, string name)
        => source.NullIfEmpty() ?? throw StandardError.Constraint($"{name} is required here.");
    [return: NotNullIfNotNull("source")]
    public static string? RequireEmpty(this string? source, string name)
        => source.IsNullOrEmpty() ? source : throw StandardError.Constraint($"{name} must be null or empty here.");
    public static string? RequireMaxLength(this string source, int length, string name)
        => source.Length <= length ? source : throw StandardError.Constraint($"{name} Must be no more than {length} characters.");

    public static SearchPhrase ToSearchPhrase(this string text, bool matchPrefixes, bool matchSuffixes)
        => new(text, matchPrefixes, matchSuffixes);

    public static string ToSentenceCase(this string str, string delimiter = " ")
        => CaseChangeRegex.Replace(str, m => $"{m.Value[0]}{delimiter}{m.Value[1..]}");

    public static string ToSnakeCase(this string input)
        => string.IsNullOrEmpty(input)
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

    public static string EnsureSuffix(this string source, string suffix)
        => source.OrdinalEndsWith(suffix) ? source : source + suffix;

    public static string Truncate(this string source, int maxLength)
        => source.Length <= maxLength ? source : source[..maxLength];
    public static string Truncate(this string source, int maxLength, string ellipsis)
        => source.Length <= maxLength ? source : source[..maxLength] + ellipsis;

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

    public static string UrlDecode(this string input)
        => WebUtility.UrlDecode(input);

    public static string HtmlEncode(this string input)
        => HtmlEncoder.Default.Encode(input);

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

    // ReSharper disable once InconsistentNaming
    public static string GetSHA1HashCode(this string input)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var inputBytes = Encoding.ASCII.GetBytes(input);
        var hashBytes = sha1.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes);
    }

    // ReSharper disable once InconsistentNaming
    public static string GetSHA256HashCode(this string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);
        return Convert.ToHexString(hashBytes);
    }
}
