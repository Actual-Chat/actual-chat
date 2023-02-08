namespace ActualChat.UI.Blazor.Services;

public readonly record struct HistoryItem(
    int Id,
    int PrevId,
    ImmutableDictionary<Type, HistoryState> States,
    Action? FollowUpAction = null
    ) : ICanBeNone<HistoryItem>, IEnumerable<KeyValuePair<Type, HistoryState>>
{
    public static HistoryItem None { get; } = default;

    public bool IsNone => Id == 0;
    public string Uri => GetState<UriState>().Uri;
    public LocalUrl LocalUrl => new(Uri);

    public HistoryState? this[Type stateType]
        => States.GetValueOrDefault(stateType);

    public HistoryItem(int id, int prevId, IEnumerable<HistoryState> states)
        : this(id, prevId, states.ToImmutableDictionary(s => s.GetType(), s => s))
    { }

    public override string ToString()
        => $"{GetType().Name}(Id: {Id}, PrevId: {PrevId})";

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<KeyValuePair<Type, HistoryState>> GetEnumerator() => States.GetEnumerator();

    public TState GetState<TState>()
        where TState : HistoryState
        => this[typeof(TState)] as TState
            ?? throw new KeyNotFoundException(typeof(TState).GetName(true));

    public bool IsIdenticalTo(HistoryItem other)
    {
        if (ReferenceEquals(States, other.States))
            return true; // Quick way to check if they're 100% equal

        foreach (var (stateType, state) in States) {
            var otherState = other[stateType];
            if (!ReferenceEquals(state, otherState) && !Equals(state, otherState))
                return false;
        }
        return true;
    }

    // "With" helpers

    public HistoryItem With<TState>(TState state)
        where TState : HistoryState
        => With(state.GetType(), state);

    public HistoryItem With(Type stateType, HistoryState state)
    {
        if (ReferenceEquals(state, null))
            throw new ArgumentNullException(nameof(state));

        var oldState = this[stateType];
        return ReferenceEquals(oldState, state)
            ? this
            : this with { States = States.SetItem(stateType, state) };
    }

    // Equality - based solely on Id
    public bool Equals(HistoryItem other) => Id == other.Id;
    public override int GetHashCode() => Id;
}
