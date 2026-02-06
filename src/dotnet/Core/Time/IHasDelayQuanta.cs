namespace ActualChat.Time;

/// <summary>
/// Provides a delay quantum for time-based operations.
/// </summary>
public interface IHasDelayQuanta
{
    // Null means "auto", i.e., computed via AutoDelayQuanta.For(delay)
    TimeSpan? DelayQuanta { get; }
}
