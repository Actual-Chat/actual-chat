using ActualLab.IO;

namespace ActualChat.UI.Blazor.Services;

public static class LogUIExt
{
    public static async Task<FilePath> DumpToTempFile(this LogUI logUI, CancellationToken cancellationToken)
    {
        var fileName = $"{MomentClockSet.Default.SystemClock.UtcNow:yyyyMMdd-HHmmss}.log";
        FilePath logFile = (FilePath)Path.GetTempPath() | fileName;
        try {
            var stream = File.Create(logFile);
            await using var _1 = stream.ConfigureAwait(false);
            await logUI.Save(stream, cancellationToken).ConfigureAwait(false);
        }
        catch {
            logFile.DeleteSilently();
            throw;
        }
        return logFile;
    }

    public static async Task Save(this LogUI logUI, Stream stream, CancellationToken cancellationToken)
    {
        var idRange = await logUI.GetIdRange(cancellationToken).ConfigureAwait(false);
        var tiles = await logUI.GetTiles(idRange, cancellationToken).ConfigureAwait(false);
        var writer = new StreamWriter(stream, leaveOpen: true);
        await using var _ = writer.ConfigureAwait(false);
        foreach (var tile in tiles)
            foreach (var entry in tile.Entries) {
                await writer.WriteLineAsync(
                        $"[{entry.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")}] {FormatLevel(entry.LogLevel)} {entry.CategoryName}: {entry.Message}"
                    )
                    .ConfigureAwait(false);
                if (entry.Exception is { } ex)
                    await writer.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
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
