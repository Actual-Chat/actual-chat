using System.IO.Compression;
using MaxMind.GeoIP2;

namespace ActualChat.Geo;

public static class GeoLiteDb
{
    private static volatile DatabaseReader? _countryDbReader;
    private static volatile DatabaseReader? _cityDbReader;

    public static DatabaseReader CountryDbReader => _countryDbReader ?? throw StandardError.Unavailable("Country DB is not ready yet.");
    public static DatabaseReader CityDbReader => _cityDbReader ?? throw StandardError.Unavailable("City DB is not ready yet.");
    public static Task<DatabaseReader> WhenCountryDbReady { get; }
    public static Task<DatabaseReader> WhenCityDbReady { get; }

    static GeoLiteDb()
    {
        WhenCountryDbReady = Task.Run(async () => {
            var reader = await Unpack("GeoLite2-Country").ConfigureAwait(false);
            Interlocked.Exchange(ref _countryDbReader, reader);
            return reader;
        });
        WhenCityDbReady = Task.Run(async () => {
            var reader = await Unpack("GeoLite2-City").ConfigureAwait(false);
            Interlocked.Exchange(ref _cityDbReader, reader);
            return reader;
        });
    }

    // Private methods

    private static async Task<DatabaseReader> Unpack(string name)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(GeoLiteDb).Assembly.Location)!;
        var zipPath = Path.Combine(assemblyDir, "data", $"{name}.zip");
        var extractDir = Path.Combine(assemblyDir, "data", name);
        var stamp = GetArchiveStamp(zipPath);
        if (!IsExtracted(extractDir, stamp)) {
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir).ConfigureAwait(false);
            await File.WriteAllTextAsync(GetStampPath(extractDir), stamp).ConfigureAwait(false);
        }

        var dbPath = Directory.GetFiles(extractDir, "*.mmdb", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException($"{name}.mmdb not found in the extracted archive.");
        return new DatabaseReader(dbPath);
    }

    private static bool IsExtracted(string extractDir, string stamp)
    {
        var stampPath = GetStampPath(extractDir);
        return File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp;
    }

    private static string GetStampPath(string extractDir)
        => Path.Combine(extractDir, ".archive-stamp");

    private static string GetArchiveStamp(string zipPath)
    {
        var info = new FileInfo(zipPath);
        return string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
    }
}
