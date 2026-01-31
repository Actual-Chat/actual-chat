namespace ActualChat.Time;

public interface IHasDelayQuanta
{
    // Null means "auto", i.e., computed via AutoDelayQuanta.For(delay)
    TimeSpan? DelayQuanta { get; }
}
