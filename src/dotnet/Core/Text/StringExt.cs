using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.RegularExpressions;

namespace ActualChat;

public static class StringExt
{
    private static readonly Regex CaseChangeRegex =
        new("([0-9a-z][A-Z])|([a-z][0-9])|([A-Z][0-9])", RegexOptions.Compiled | RegexOptions.ExplicitCapture);
#pragma warning disable MA0023
    private static readonly Regex CamelCaseRegex =
        new (@"([a-z0-9])([A-Z])", RegexOptions.Compiled);
#pragma warning restore MA0023

    public static string RequireNonEmpty(this string? source, string name)
        => source.NullIfEmpty() ?? throw StandardError.Constraint($"{name} is required here.");
    [return: NotNullIfNotNull("source")]
    public static string? RequireEmpty(this string? source, string name)
        => source.IsNullOrEmpty() ? source : throw StandardError.Constraint($"{name} must be null or empty here.");

    public static SearchPhrase ToSearchPhrase(this string text)
        => new(text);

    public static string ToSentenceCase(this string str, string delimiter = " ")
        => CaseChangeRegex.Replace(str, m => $"{m.Value[0]}{delimiter}{m.Value[1..]}");

    public static string ToSnakeCase(this string input)
        => string.IsNullOrEmpty(input)
            ? input
            : CamelCaseRegex.Replace(input, "$1_$2")
                .ToLower(CultureInfo.InvariantCulture)
                .OrdinalReplace("__", "_");

    public static string Capitalize(this string source)
        => source.IsNullOrEmpty() ? source : source[..1].ToUpperInvariant() + source[1..];

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

    public static string UrlEncode(this string input)
        => WebUtility.UrlEncode(input);

    public static string UrlDecode(this string input)
        => WebUtility.UrlDecode(input);

    public static string EnsureSuffix(this string input, string suffix, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        => input.EndsWith(suffix, comparison) ? input : input + suffix;
}
