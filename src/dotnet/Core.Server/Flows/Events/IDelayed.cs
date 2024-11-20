namespace ActualChat.Flows;

public interface IDelayed
{
    Moment? DelayUntil { get; init; }
}
