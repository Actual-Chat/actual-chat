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
        try {
            var ianaTimeZone = TZConvert.WindowsToIana(windowsTimeZone);
            return Task.FromResult(ianaTimeZone);
        }
        catch (InvalidTimeZoneException e) {
            log.LogWarning(e, "Failed to converter Windows time zone to Iana");
        }
        return Task.FromResult("");
    }
}
