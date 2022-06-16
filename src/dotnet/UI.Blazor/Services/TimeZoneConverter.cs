using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.UI.Blazor.Services;

public abstract class TimeZoneConverter
{
    protected ILogger Log { get; }

    public Task WhenInitialized { get; init; } = null!;

    protected TimeZoneConverter(ILogger? log = null)
        => Log = log ?? NullLogger.Instance;

    public DateTime ToLocalTime(Moment moment)
        => ToLocalTime(moment.ToDateTime());

    public abstract DateTime ToLocalTime(DateTime utcTime);

    protected DateTime AssertUtcTime(DateTime utcTime)
    {
        if (utcTime.Kind == DateTimeKind.Utc)
            return utcTime;
        Log.LogWarning("Expected UTC time, but got '{DateTime}'", utcTime);
        return utcTime.ToUniversalTime();
    }
}

public sealed class ClientSizeTimeZoneConverter : TimeZoneConverter
{
    public ClientSizeTimeZoneConverter(ILogger<ClientSizeTimeZoneConverter> log) : base(log)
        => WhenInitialized = Task.CompletedTask;

    public override DateTime ToLocalTime(DateTime utcTime)
    {
        utcTime = AssertUtcTime(utcTime);
        return utcTime.ToLocalTime();
    }
}

public sealed class ServerSideTimeZoneConverter : TimeZoneConverter
{
    private readonly TaskSource<Unit> _whenInitializedSource;
    private TimeSpan _utcOffset;

    public ServerSideTimeZoneConverter(ILogger<ServerSideTimeZoneConverter> log) : base(log)
    {
        _whenInitializedSource = TaskSource.New<Unit>(true);
        WhenInitialized = _whenInitializedSource.Task;
    }

    public void Initialize(TimeSpan utcOffset)
    {
        _utcOffset = utcOffset;
        _whenInitializedSource.TrySetResult(default);
    }

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
}
