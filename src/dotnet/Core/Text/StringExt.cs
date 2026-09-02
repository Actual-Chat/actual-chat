using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CommunityToolkit.HighPerformance;

namespace ActualChat;

public static partial class StringExt
{
    [GeneratedRegex("([0-9a-z][A-Z])|([a-z][0-9])|([A-Z][0-9])", RegexOptions.ExplicitCapture)]
    private static partial Regex CaseChangeRegexFactory();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex CamelCaseRegexFactory();

    [GeneratedRegex(@"(?<line>[^\r\n]*)\r?\n", RegexOptions.ExplicitCapture)]
    private static partial Regex NewLineRegexFactory();

    [GeneratedRegex(@"\s*(\S+)\s*$")]
    private static partial Regex LastWordRegexFactory();

    [GeneratedRegex(@"([a-z0-9])([A-Z])|([A-Z])([A-Z][a-z])")]
    private static partial Regex KebabCaseRegexFactory();

    private static readonly Regex CaseChangeRegex = CaseChangeRegexFactory();
#pragma warning disable MA0023
    private static readonly Regex CamelCaseRegex = CamelCaseRegexFactory();
    private static readonly Regex NewLineRegex = NewLineRegexFactory();
#pragma warning restore MA0023
    private static readonly Regex LastWordRegex = LastWordRegexFactory();
    private static readonly Regex KebabCaseRegex = KebabCaseRegexFactory();

    public static string RequireNonEmpty(this string? source, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.NullIfEmpty() ?? throw StandardError.Constraint($"{name} is required here.");
    [return: NotNullIfNotNull(nameof(source))]
    public static string? RequireNotEqual(this string? source, string target, [CallerArgumentExpression(nameof(source))] string name = "")
        => source == target
            ? throw StandardError.Constraint($"{name} should not be {target}.")
            : source;
    [return: NotNullIfNotNull(nameof(source))]
    public static string? RequireEmpty(this string? source, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.IsNullOrEmpty() ? source : throw StandardError.Constraint($"{name} must be null or empty here.");
    public static string? RequireMaxLength(this string source, int length, [CallerArgumentExpression(nameof(source))] string name = "")
        => source.Length <= length ? source : throw StandardError.Constraint($"{name} Must be no more than {length} characters.");

    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? source)
        => string.IsNullOrWhiteSpace(source);

    public static string ToSentenceCase(this string str, string delimiter = " ")
        => CaseChangeRegex.Replace(str, m => $"{m.Value[0]}{delimiter}{m.Value[1..]}");

    public static string ToSnakeCase(this string input)
        => input.IsNullOrEmpty()
            ? input
            : CamelCaseRegex.Replace(input, "$1_$2")
                .ToLower()
                .Replace("__", "_");

    public static string ToKebabCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Split on capital letters, but keep sequences of capitals together if followed by lowercase
        // e.g., "XMLHttpRequest" → ["XML", "Http", "Request"]
        string withDashes = KebabCaseRegex.Replace(
            input,
            "$1$3-$2$4"
        );

        return withDashes.ToLower();
    }

    public static string Capitalize(this string source)
        => source.IsNullOrEmpty() ? source : source.Capitalize(0);

    public static string Capitalize(this string source, int position)
        => ChangeCase(source, position, char.ToUpper);

    public static string Decapitalize(this string source)
        => source.IsNullOrEmpty() ? source : source.Decapitalize(0);

    public static string Decapitalize(this string source, int position)
        => ChangeCase(source, position, char.ToLower);

    private static string ChangeCase(string source, int position, Func<char, char> changeCase)
    {
        var firstLetter = source[position];
        var firstLetterChanged = changeCase(firstLetter);
        if (firstLetter == firstLetterChanged)
            return source;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        if (position > 0)
            sb.Append(source.AsSpan(0, position));
        sb.Append(firstLetterChanged);
        sb.Append(source.AsSpan(position + 1));
        return sb.ToStringAndRelease();
    }

    public static SanitizedString<Sanitizers.PrefixAndLengthHint> ToPrivate(this string source)
        => new(source);

    public static string Pluralize(this string source, int count)
        => count == 1 ? source : source + "s";

    public static string DotPrepend(this string source, string? prefix, char separator = '.')
        => prefix.IsNullOrEmpty()
            ? source
            : $"{prefix}{separator}{source}";

    public static string DotAppend(this string source, string? suffix, char separator = '.')
        => suffix.IsNullOrEmpty()
            ? source
            : $"{source}{separator}{suffix}";

    public static string EnsurePrefix(this string source, string prefix)
        => source.StartsWith(prefix) ? source : prefix + source;

    public static string EnsureSuffix(this string source, string suffix)
        => source.EndsWith(suffix) ? source : source + suffix;

    public static string Truncate(this string source, int maxLength)
        => source.Length <= maxLength ? source : source[..maxLength];
    public static string Truncate(this string source, int maxLength, string ellipsis)
        => source.Length <= maxLength ? source : source[..maxLength] + ellipsis;

    public static string TrimNonLetterOrDigits(this string s)
    {
        var iStart = s.FirstIndexOf(char.IsLetterOrDigit);
        var iEnd = s.LastIndexOf(char.IsLetterOrDigit);
        return iStart < 0 || iEnd < 0 ? "" : s[iStart..(iEnd + 1)];
    }

    public static string Suffix(this string s, string separator)
    {
        if (s.IsNullOrEmpty())
            return s;
        var i = s.LastIndexOf(separator);
        return i < 0 ? s : s[(i + separator.Length)..];
    }

    public static string Suffix(this string s, string separator, StringComparison comparison)
    {
        if (s.IsNullOrEmpty())
            return s;
        var i = s.LastIndexOf(separator, comparison);
        return i < 0 ? s : s[(i + separator.Length)..];
    }

    public static bool HasPrefix(this string source, string prefix, out string suffix)
    {
        if (source.StartsWith(prefix)) {
            suffix = source[prefix.Length..];
            return true;
        }
        suffix = "";
        return false;
    }

    public static bool HasPrefix(this string source, string prefix, StringComparison stringComparison, out string suffix)
    {
        if (source.StartsWith(prefix, stringComparison)) {
            suffix = source[prefix.Length..];
            return true;
        }
        suffix = "";
        return false;
    }

    public static string[] SplitIntoWords(this string text) =>
        text.Split([' ', ',', '!', '.', ':', '-'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static int GetPrefixCharCount(this string source, char prefix)
    {
        for (var i = 0; i < source.Length; i++)
            if (source[i] != prefix)
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
                yield return (match.Groups["line"].Value, true);
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

    // "null encoding" means "return string byte span", i.e. UTF16 encoding
    public static ReadOnlySpan<byte> Encode(this string value, Encoding? encoding)
        => encoding?.GetBytes(value) ?? value.Utf16Encode();
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> Utf16Encode(this string value)
        => value.AsSpan().Cast<char, byte>();

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

    public static (string Head, string? Last) SplitLastWord(this string text)
    {
        if (text.IsNullOrEmpty())
            return ("", null);

        var match = LastWordRegex.Match(text);
        if (!match.Success)
            return (text, null);

        var lastWord = match.Groups[1].Value;
        var prefix = text[..match.Index];
        return (prefix, lastWord);
    }

    // ParseXxx

    public static (string Host, ushort Port) ParseHostPort(this string hostPortOrUrl, ushort defaultPort)
    {
        var (host, port) = hostPortOrUrl.ParseHostPort();
        port ??= defaultPort;
        return (host, port.GetValueOrDefault());
    }

    public static (string Host, ushort? Port) ParseHostPort(this string hostPortOrUrl)
    {
        if (!hostPortOrUrl.TryParseHostPort(out var host, out var port))
            throw new ArgumentOutOfRangeException(nameof(hostPortOrUrl),
                $"This string should have 'host[:port]' format: '{hostPortOrUrl}'");
        return (host, port);
    }

    public static bool TryParseHostPort(
        this string hostPortOrUrl,
        out string host,
        out ushort? port)
    {
        if (Uri.TryCreate(hostPortOrUrl, UriKind.Absolute, out var uri)) {
            host = uri.Host;
            port = uri.IsDefaultPort ? null : (ushort)uri.Port;
            return true;
        }

        host = "";
        port = null;
        if (hostPortOrUrl.IsNullOrEmpty())
            return false;

        var columnIndex = hostPortOrUrl.IndexOf(':');
        if (columnIndex <= 0) {
            host = hostPortOrUrl;
            return true;
        }

        host = hostPortOrUrl[..columnIndex];
        var portStr = hostPortOrUrl[(columnIndex + 1)..];
        if (portStr.IsNullOrEmpty())
            return true;

        if (!ushort.TryParse(portStr, NumberStyles.Integer, null, out var portValue))
            return false;

        port = portValue;
        return true;
    }

    // To/FromBaseXX

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

    // NewLines

    public static string NormalizeNewLines(this string source, string newLine)
    {
        if (source.Length == 0 || IsNewLineNormalized(source, newLine))
            return source;

        var sb = ActualLab.Text.StringBuilderExt.Acquire(source.Length + 16);
        for (var i = 0; i < source.Length; i++) {
            var c = source[i];
            if (c == '\r') {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                    i++;
                sb.Append(newLine);
            }
            else if (c == '\n')
                sb.Append(newLine);
            else
                sb.Append(c);
        }

        return sb.ToStringAndRelease();

        static bool IsNewLineNormalized(string source, string newLine) {
            for (var i = 0; i < source.Length; i++) {
                var c = source[i];
                if (c != '\r' && c != '\n')
                    continue;
                if (i + newLine.Length > source.Length || !source.AsSpan(i, newLine.Length).SequenceEqual(newLine))
                    return false;

                i += newLine.Length - 1;
            }

            return true;
        }
    }

    // Indent

    public static string Indent(this string s, string indent)
    {
        if (s.Length == 0 || indent.Length == 0)
            return s;

        // Count inner newlines (not trailing) to calculate result size
        var sourceSpan = s.AsSpan();
        var innerNewlineCount = 0;
        var lastNewlineIndex = -1;
        for (var i = 0; i < sourceSpan.Length; i++) {
            if (sourceSpan[i] != '\n')
                continue;

            lastNewlineIndex = i;
            if (i < sourceSpan.Length - 1)
                innerNewlineCount++;
        }

        // No newlines at all - just prepend indent
        if (lastNewlineIndex < 0)
            return indent + s;

        // indent for first line + indent after each inner \n
        var resultLength = s.Length + indent.Length * (1 + innerNewlineCount);
        return string.Create(resultLength, (source: s, indent), static (span, state) => {
            var (src, ind) = state;
            var srcSpan = src.AsSpan();
            var indSpan = ind.AsSpan();
            var pos = 0;

            // Prepend indent to first line
            indSpan.CopyTo(span[pos..]);
            pos += indSpan.Length;

            for (var i = 0; i < srcSpan.Length; i++) {
                var c = srcSpan[i];
                span[pos++] = c;
                if (c == '\n' && i < srcSpan.Length - 1) {
                    indSpan.CopyTo(span[pos..]);
                    pos += indSpan.Length;
                }
            }
        });
    }
}
