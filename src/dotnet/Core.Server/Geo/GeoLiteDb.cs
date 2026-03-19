using System.IO.Compression;
using MaxMind.GeoIP2;

namespace ActualChat.Geo;

public static class GeoLiteDb
{
    private static volatile DatabaseReader? _reader;

    public static DatabaseReader Reader => _reader ?? throw StandardError.Unavailable("GeoLiteDb is not ready yet.");
    public static Task WhenReady { get; }

    static GeoLiteDb()
        => WhenReady = Task.Run(Unpack);

    // Private methods

    private static async Task Unpack()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(GeoLiteDb).Assembly.Location)!;
        var zipPath = Path.Combine(assemblyDir, "data", "GeoLite2-Country.zip");
        var extractDir = Path.Combine(assemblyDir, "data", "GeoLite2-Country");
        if (!Directory.Exists(extractDir))
            await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir).ConfigureAwait(false);

        var dbPath = Directory.GetFiles(extractDir, "*.mmdb", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException("GeoLite2-Country.mmdb not found in the extracted archive.");
        var reader = new DatabaseReader(dbPath);
        Interlocked.Exchange(ref _reader, reader);
    }
}
