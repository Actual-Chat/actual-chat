namespace ActualChat.Hardware;

public interface ISleepDurationProvider
{
    IState<TimeSpan> TotalSleepDuration { get; }
}
