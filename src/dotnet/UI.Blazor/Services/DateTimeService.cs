namespace ActualChat.UI.Blazor.Services;

public class DateTimeService
{
    private readonly ILogger<DateTimeService> _log;
    private TimeSpan? _timeOffset;

    public DateTimeService(ILogger<DateTimeService> log)
        => _log = log;

    public DateTime ToLocalTime(Moment moment)
        => ToLocalTime(moment.ToDateTime());

    public DateTime ToLocalTime(DateTime dateTime)
    {
        if (dateTime.Kind != DateTimeKind.Utc) {
            _log.LogWarning("Given dateTime is not Utc time. Given value is {DateTime}", dateTime);
            return dateTime.ToLocalTime();
        }
        if (_timeOffset == null)
            return dateTime.ToLocalTime();
        // this solution does not properly work with daylight saving time
        return dateTime + _timeOffset.Value;
    }

    public void SetupTimeOffset(TimeSpan timeOffset)
        => _timeOffset = timeOffset;
}
