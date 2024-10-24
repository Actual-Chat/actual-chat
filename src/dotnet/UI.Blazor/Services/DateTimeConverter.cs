namespace ActualChat.UI.Blazor.Services;

public abstract class DateTimeConverter
{
    protected ILogger Log { get; }

    protected DateTimeConverter(IServiceProvider services)
        => Log = services.LogFor(GetType());

    public DateTime ToLocalTime(Moment moment)
        => ToLocalTimeAssumeUtc(moment.ToDateTime());

    public abstract DateTime ToLocalTime(DateTime utcTime);
    public abstract DateTime ToLocalTimeAssumeUtc(DateTime utcTime);

    protected DateTime AssertUtcTime(DateTime utcTime)
    {
        if (utcTime.Kind == DateTimeKind.Utc)
            return utcTime;

        Log.LogWarning("Expected UTC time, but got '{DateTime}'", utcTime);
        return utcTime.ToUniversalTime();
    }
}

public sealed class ClientSizeDateTimeConverter(IServiceProvider services) : DateTimeConverter(services)
{
    public override DateTime ToLocalTime(DateTime utcTime)
    {
        utcTime = AssertUtcTime(utcTime);
        return utcTime.ToLocalTime();
    }

    public override DateTime ToLocalTimeAssumeUtc(DateTime utcTime)
        => utcTime.ToLocalTime();
}

public sealed class ServerSideDateTimeConverter(IServiceProvider services) : DateTimeConverter(services)
{
    private TimeSpan _utcOffset;

    public void Initialize(TimeSpan utcOffset)
        => _utcOffset = utcOffset;

    public override DateTime ToLocalTime(DateTime utcTime)
    {
        // TODO(DF): This implementation does not properly handle DST change!

        // Ideally we want to throw an error when !WhenInitialized.IsCompleted,
        // but since we assume WhenInitialized is used in Component.OnInitializedAsync,
        // re-render will be triggered anyway for anything that was rendered
        // w/ the wrong timezone.

        utcTime = AssertUtcTime(utcTime);
        return utcTime - _utcOffset;
    }

    public override DateTime ToLocalTimeAssumeUtc(DateTime utcTime)
        // TODO(DF): Same issue as w/ above method
        => utcTime - _utcOffset;
}
