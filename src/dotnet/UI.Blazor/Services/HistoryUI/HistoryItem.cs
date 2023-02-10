using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public sealed record HistoryItem(
    int Id,
    int PrevId,
    string Uri,
    ImmutableDictionary<Type, HistoryState> States,
    Action? OnNavigate = null
    ) : IEnumerable<KeyValuePair<Type, HistoryState>>
{
    public int BackStepCount => States.Values.Sum(s => s.BackStepCount);
    public bool HasBackSteps => States.Values.Any(s => s.BackStepCount > 0);

    public HistoryState? this[Type stateType]
        => States.GetValueOrDefault(stateType);

    public HistoryItem(
        int id, int prevId,
        NavigationManager nav,
        ImmutableDictionary<Type, HistoryState> states,
        Action? followUpAction = null)
        : this(id, prevId, nav.GetLocalUrl().Value, states, followUpAction)
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
        if (ReferenceEquals(States, other.States)) // Quick way to check if they're 100% equal
            return OrdinalEquals(Uri, other.Uri);

        foreach (var (stateType, state) in States) {
            var otherState = other[stateType];
            if (!ReferenceEquals(state, otherState) && !Equals(state, otherState))
                return false;
        }
        return OrdinalEquals(Uri, other.Uri);
    }

    public int CompareBackStepCount(HistoryItem otherItem)
    {
        var hasStatesWithMoreBackSteps = false;
        var hasStatesWithLessBackSteps = false;
        foreach (var (stateType, state) in States) {
            var otherState = otherItem[stateType];
            var backStepCount = state.BackStepCount;
            var otherBackStepCount = otherState?.BackStepCount ?? 0;
            if (backStepCount > otherBackStepCount)
                hasStatesWithMoreBackSteps = true;
            else if (backStepCount < otherBackStepCount)
                hasStatesWithLessBackSteps = true;
        }
        return hasStatesWithMoreBackSteps
            ? hasStatesWithLessBackSteps ? 0 : 1
            : hasStatesWithLessBackSteps ? -1 : 0;
    }

    public IEnumerable<HistoryStateChange> GetChanges(HistoryItem prevItem)
    {
        foreach (var (stateType, state) in States) {
            var prevState = prevItem[stateType];
            var change = new HistoryStateChange(state, prevState!);
            if (change.HasChanges)
                yield return change;
        }
    }

    // "With" helpers

    public HistoryItem WithUri(NavigationManager nav)
        => WithUri(nav.GetLocalUrl().Value);
    public HistoryItem WithUri(string uri)
        => OrdinalEquals(uri, Uri) ? this : this with { Uri = uri };

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

    // This record relies on referential equality
    public bool Equals(HistoryItem? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
