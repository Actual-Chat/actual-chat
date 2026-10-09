using Microsoft.JSInterop;
using TimeZoneConverter;

namespace ActualChat.Mui;

// Dates are stored and queried in UTC; the UI shows them in the browser's time zone
public sealed class MuiTime(IJSRuntime js)
{
    private bool _isInitialized;

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;
    public string TimeZoneName { get; private set; } = "UTC";

    public async Task EnsureInitialized()
    {
        if (_isInitialized)
            return;

        _isInitialized = true;
        try {
            var id = await js.InvokeAsync<string>("muiGetTimeZone");
            // TZConvert resolves IANA ids without ICU, unlike TimeZoneInfo.FindSystemTimeZoneById
            if (!id.IsNullOrEmpty() && TZConvert.TryGetTimeZoneInfo(id, out var timeZone)) {
                TimeZone = timeZone;
                TimeZoneName = id;
            }
        }
        catch {
            // Without the browser's zone everything is shown in UTC
        }
    }

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    public DateTime ToUtc(DateTime local)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    public string Format(DateTime utc, string format = "yyyy-MM-dd HH:mm")
        => ToLocal(utc).ToString(format);
}
