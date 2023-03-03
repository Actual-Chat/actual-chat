namespace ActualChat.UI.Blazor.Services.Internal;

[StructLayout(LayoutKind.Auto)]
public readonly record struct HistoryStateChange(
    HistoryState State,
    HistoryState PrevState)
{
    public bool HasChanges => !ReferenceEquals(State, PrevState) && !Equals(State, PrevState);

    public override string ToString()
        => $"{State.Name}({PrevState.Format()} -> {State.Format()})";
}
