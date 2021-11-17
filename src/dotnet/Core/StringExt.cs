namespace ActualChat;

public static class StringExt
{
    // NOTE(AY): Remove if stays unused
    public static (string Host, ushort? Port) ParseHostPort(this string hostPort)
    {
        if (!hostPort.TryParseHostPort(out var host, out var port))
            throw new ArgumentOutOfRangeException(nameof(hostPort),
                "Input string should have 'host[:port]' format.");
        return (host, port);
    }

    // NOTE(AY): Remove if stays unused
    public static bool TryParseHostPort(
        this string hostPort,
        out string host,
        out ushort? port)
    {
        host = "";
        port = null;
        if (hostPort.IsNullOrEmpty())
            return false;

        var columnIndex = hostPort.IndexOf(":", StringComparison.Ordinal);
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
}
