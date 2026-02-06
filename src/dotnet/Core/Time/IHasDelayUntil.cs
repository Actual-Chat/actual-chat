namespace ActualChat.Time;

/// <summary>
/// Indicates an object has a scheduled delay until a specific moment.
/// </summary>
public interface IHasDelayUntil
{
    Moment DelayUntil { get; }
}
