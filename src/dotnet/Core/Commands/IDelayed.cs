namespace ActualChat;

public interface IDelayed
{
    Moment? DelayUntil { get; init; }
}
