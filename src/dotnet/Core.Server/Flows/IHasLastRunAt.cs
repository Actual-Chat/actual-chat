namespace ActualChat.Flows;

public interface IHasLastRunAt
{
    Moment LastRunAt { get; }
}
