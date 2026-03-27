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
        WhenCountryDbReady = Task.Run(UnpackCountry);
        WhenCityDbReady = Task.Run(UnpackCity);
    }

    // Private methods

    private static async Task<DatabaseReader> UnpackCountry()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(GeoLiteDb).Assembly.Location)!;
        var zipPath = Path.Combine(assemblyDir, "data", "GeoLite2-Country.zip");
        var extractDir = Path.Combine(assemblyDir, "data", "GeoLite2-Country");
        if (!Directory.Exists(extractDir))
            await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir).ConfigureAwait(false);

        var dbPath = Directory.GetFiles(extractDir, "*.mmdb", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException("GeoLite2-Country.mmdb not found in the extracted archive.");
        var reader = new DatabaseReader(dbPath);
        Interlocked.Exchange(ref _countryDbReader, reader);
        return reader;
    }

    private static async Task<DatabaseReader> UnpackCity()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(GeoLiteDb).Assembly.Location)!;
        var zipPath = Path.Combine(assemblyDir, "data", "GeoLite2-City.zip");
        var extractDir = Path.Combine(assemblyDir, "data", "GeoLite2-City");
        if (!Directory.Exists(extractDir))
            await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir).ConfigureAwait(false);

        var dbPath = Directory.GetFiles(extractDir, "*.mmdb", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new FileNotFoundException("GeoLite2-City.mmdb not found in the extracted archive.");
        var reader = new DatabaseReader(dbPath);
        Interlocked.Exchange(ref _cityDbReader, reader);
        return reader;
    }
}
