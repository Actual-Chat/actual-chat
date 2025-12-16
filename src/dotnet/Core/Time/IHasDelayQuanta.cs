namespace ActualChat.Time;

public interface IHasDelayQuanta : IHasDelayUntil
{
    TimeSpan DelayQuanta { get; }
}
