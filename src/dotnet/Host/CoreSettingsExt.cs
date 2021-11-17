using ActualChat.Module;

namespace ActualChat.Host;

internal static class CoreSettingsExt
{
    public static (string Host, ushort Port)? ParseOtlpEndpoint(this CoreSettings? settings)
    {
        if (string.IsNullOrWhiteSpace(settings?.OtlpEndpoint))
            return null;

        var idx = settings.OtlpEndpoint.IndexOf(":", StringComparison.Ordinal);
        if (idx <= 0 || idx == settings.OtlpEndpoint.Length - 1) {
            return (settings.OtlpEndpoint, 4317);
        }
        var (host, portStr) = (settings.OtlpEndpoint[..idx], settings.OtlpEndpoint[++idx..]);

        if (!ushort.TryParse(portStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)) {
            return null;
        }
        return (host, port);
    }
}
