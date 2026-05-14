using System.Text;
using ActualChat.Hosting;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Serialises a snapshot of <see cref="LogEntry"/> items plus environment
/// metadata into a UTF-8 plain-text payload suitable for a diagnostic report.
/// </summary>
public static class ReportLogBuilder
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static byte[] BuildLogPayload(
        IReadOnlyList<LogEntry> entries,
        HostInfo hostInfo,
        AccountFull? account,
        Moment generatedAt)
    {
        var sb = new StringBuilder(64 * 1024);
        AppendHeader(sb, entries.Count, hostInfo, account, generatedAt);
        foreach (var entry in entries)
            AppendEntry(sb, entry);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // Private methods

    private static void AppendHeader(
        StringBuilder sb,
        int entryCount,
        HostInfo hostInfo,
        AccountFull? account,
        Moment generatedAt)
    {
        sb.Append("=== ").Append(CoreConstants.AppName).Append(" log report ===").Append('\n');
        sb.Append("Generated:    ").Append(generatedAt.ToString(TimestampFormat)).Append('\n');
        sb.Append("App:          ")
            .Append("HostKind=").Append(hostInfo.HostKind)
            .Append(", AppKind=").Append(hostInfo.AppKind)
            .Append(", Environment=").Append(hostInfo.Environment.Value)
            .Append(", DeviceModel=").Append(hostInfo.DeviceModel)
            .Append('\n');
        sb.Append("BaseUrl:      ").Append(hostInfo.BaseUrl).Append('\n');
        sb.Append("Account:      ");
        if (account is { } a && a.HasId())
            sb.Append(a.Id.Value).Append("  ").Append(a.Name);
        else
            sb.Append("(none)");
        sb.Append('\n');
        sb.Append("Entries:      ").Append(entryCount).Append('\n');
        sb.Append('\n');
    }

    private static void AppendEntry(StringBuilder sb, LogEntry entry)
    {
        sb.Append('[').Append(entry.Timestamp.ToString(TimestampFormat)).Append("] ");
        sb.Append(FormatLevel(entry.LogLevel)).Append(' ');
        sb.Append(entry.CategoryName).Append(": ");
        sb.Append(entry.Message).Append('\n');
        if (entry.Exception is { } ex) {
            foreach (var line in ex.ToString().Split('\n')) {
                sb.Append("  ").Append(line.TrimEnd('\r')).Append('\n');
            }
        }
    }

    private static string FormatLevel(LogLevel level)
        => level switch {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "FATAL",
            _ => "?????",
        };
}
