namespace ActualChat.Time;

public interface IHasDelayUntilQuanta : IHasDelayUntil
{
    TimeSpan DelayQuanta { get; }
}
