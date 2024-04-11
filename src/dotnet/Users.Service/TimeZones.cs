using TimeZoneNames;

namespace ActualChat.Users;

public class TimeZones : ITimeZones
{
    // [ComputeMethod]
    public virtual Task<ApiArray<TimeZone>> List(string languageCode, CancellationToken cancellationToken)
    {
        var zones = TZNames.GetDisplayNames(languageCode, true).Select(x => new TimeZone(x.Key) { IanaName = x.Value }).ToApiArray();
        return Task.FromResult(zones);
    }
}
