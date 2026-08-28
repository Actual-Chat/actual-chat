using TimeZoneConverter;
using TimeZoneNames;

namespace ActualChat.Users;

public class TimeZones(ILogger<TimeZones> log) : ITimeZones
{
    // [ComputeMethod]
    public virtual Task<TimeZone[]> List(string languageCode, CancellationToken cancellationToken)
    {
        var (countryNames, zoneToCountry) = TryBuildCountryData(languageCode);
        var now = DateTimeOffset.UtcNow;
        var zones = TZNames.GetDisplayNames(languageCode, true)
            // Keep only Continent/City zones; this drops the Etc/GMT+N pseudo-zones
            // (which surface as "GMT+11" city names) and single-token aliases like CET.
            .Where(x => x.Key.Contains('/', StringComparison.Ordinal) && !x.Key.StartsWith("Etc/", StringComparison.Ordinal))
            .Select(x => {
                var countryCode = zoneToCountry.GetValueOrDefault(x.Key, "");
                var countryName = !countryCode.IsNullOrEmpty() && countryNames.TryGetValue(countryCode, out var name) ? name : "";
                return new TimeZone(x.Key) {
                    IanaName = x.Value,
                    City = ToCity(x.Key),
                    UtcOffsetMinutes = GetUtcOffsetMinutes(x.Key, now),
                    CountryCode = countryCode,
                    CountryName = countryName,
                };
            })
            .OrderBy(z => z.UtcOffsetMinutes)
            .ThenBy(z => z.City, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(zones);
    }

    // TimeZoneNames' country APIs build a CultureInfo internally, which throws under
    // globalization-invariant mode (the Blazor WASM default). Degrade to no country there.
    private (IDictionary<string, string> CountryNames, Dictionary<string, string> ZoneToCountry) TryBuildCountryData(string languageCode)
    {
        try {
            var countryNames = TZNames.GetCountryNames(languageCode);
            var zoneToCountry = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var countryCode in countryNames.Keys)
                foreach (var zoneId in TZNames.GetTimeZoneIdsForCountry(countryCode))
                    zoneToCountry[zoneId] = countryCode; // Multi-country zones are rare; last one wins
            return (countryNames, zoneToCountry);
        }
        catch (Exception e) {
            log.LogWarning(e, "Country names unavailable (globalization-invariant mode?); time zones will omit country.");
            return (new Dictionary<string, string>(), new Dictionary<string, string>(StringComparer.Ordinal));
        }
    }

    // TZConvert resolves IANA ids cross-platform without ICU, unlike
    // TimeZoneInfo.FindSystemTimeZoneById, which needs Windows ids on a Windows host
    // and fails for IANA ids under globalization-invariant mode.
    private int GetUtcOffsetMinutes(string ianaId, DateTimeOffset at)
    {
        if (TZConvert.TryGetTimeZoneInfo(ianaId, out var timeZoneInfo))
            return (int)timeZoneInfo.GetUtcOffset(at).TotalMinutes;

        log.LogWarning("Failed to resolve UTC offset for time zone '{TimeZoneId}'.", ianaId);
        return 0;
    }

    private static string ToCity(string ianaId)
    {
        var slash = ianaId.LastIndexOf('/');
        var tail = slash >= 0 ? ianaId[(slash + 1)..] : ianaId;
        return tail.Replace('_', ' ');
    }

    // [ComputeMethod]
    public virtual Task<string> ConvertWindowsToIana(string windowsTimeZone, CancellationToken cancellationToken)
    {
        if (TZConvert.TryWindowsToIana(windowsTimeZone, out var ianaTimeZoneName))
            return Task.FromResult(ianaTimeZoneName);

        log.LogWarning("Failed to converter Windows time zone to Iana. Time zone: '{TimeZoneId}'.", windowsTimeZone);
        return Task.FromResult("");
    }

    // [ComputeMethod]s
    public virtual Task<string> FindDisplayName(string languageCode, string timeZoneId, CancellationToken cancellationToken)
    {
        var displayName = TZNames.GetDisplayNameForTimeZone(timeZoneId, languageCode);
        if (displayName is not null)
            return Task.FromResult(displayName);

        log.LogWarning("Unable to find a name for a time zone. Time zone: '{TimeZoneId}'.", timeZoneId);
        return Task.FromResult("");
    }
}
