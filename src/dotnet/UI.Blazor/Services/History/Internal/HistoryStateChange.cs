namespace ActualChat.UI.Blazor.Services.Internal;

public sealed record HistoryStateChange(
    HistoryState State,
    HistoryState PrevState)
{
    public bool HasChanges => !ReferenceEquals(State, PrevState) && !Equals(State, PrevState);

    public override string ToString()
        => $"{State.Name}({PrevState.Format()} -> {State.Format()})";
}
