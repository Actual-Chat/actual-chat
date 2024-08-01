using TimeZoneConverter;
using TimeZoneNames;

namespace ActualChat.Users;

public class TimeZones(ILogger<TimeZones> log) : ITimeZones
{
    // [ComputeMethod]
    public virtual Task<ApiArray<TimeZone>> List(string languageCode, CancellationToken cancellationToken)
    {
        var zones = TZNames.GetDisplayNames(languageCode, true)
            .Select(x => new TimeZone(x.Key) { IanaName = x.Value })
            .ToApiArray();
        return Task.FromResult(zones);
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
