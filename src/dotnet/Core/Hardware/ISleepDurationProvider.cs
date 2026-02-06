namespace ActualChat.Hardware;

/// <summary>
/// Provides total device sleep duration for detecting sleep/wake cycles.
/// </summary>
public interface ISleepDurationProvider
{
    IState<TimeSpan> TotalSleepDuration { get; }
}
