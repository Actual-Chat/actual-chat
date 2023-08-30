using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public class HistoryStepper
{
    private ImmutableList<HistoryStepRef> _refs = ImmutableList<HistoryStepRef>.Empty;

    private History History { get; }
    public IReadOnlyCollection<HistoryStepRef> Refs => _refs;

    public HistoryStepper(IServiceProvider services)
    {
        History = services.GetRequiredService<History>();
        History.Register(new OwnHistoryState(this, ImmutableSortedSet<Symbol>.Empty));
    }

    public HistoryStepRef StepIn(string owner)
    {
        var modalRef = new HistoryStepRef(owner, this);
        _refs = _refs.Add(modalRef);
        History.Save<OwnHistoryState>();
        return modalRef;
    }

    public HistoryStepRef? FindRef(Symbol id)
        => Refs.SingleOrDefault(x => x.Id == id);

    public void Close(Symbol id)
    {
        var stepRef = FindRef(id);
        if (stepRef == null)
            return;

        CloseInternal(stepRef);

        History.Save<OwnHistoryState>();
    }

    private void CloseInternal(HistoryStepRef modalRef)
    {
        _refs = _refs.Remove(modalRef);
        ((IHistoryStepRefImpl)modalRef).MarkClosed();
    }

    // Nested types

    private sealed record OwnHistoryState(HistoryStepper Host, IReadOnlyList<Symbol> StepIds) : HistoryState
    {
        public override int BackStepCount => StepIds.Count;
        public override bool IsUriDependent => true;

        public override string Format()
            => StepIds.ToDelimitedString();

        public override HistoryState Save()
            => this with {
                StepIds = Host._refs.Select(x => x.Id).ToList(),
            };

        public override void Apply(HistoryTransition transition)
        {
            var isHistoryMove = transition.LocationChangeKind is LocationChangeKind.HistoryMove;
            var stepIds = isHistoryMove ? StepIds : ImmutableSortedSet<Symbol>.Empty;
            var stepRefs = Host._refs;
            for (var i = stepRefs.Count - 1; i >= 0; i--) {
                var stepRef = stepRefs[i];
                if (!stepIds.Contains(stepRef.Id))
                    Host.CloseInternal(stepRef);
            }
        }

        public override HistoryState? Back()
            => StepIds.Count == 0
                ? null
                : this with { StepIds = StepIds.Take(StepIds.Count - 1).ToList() };

        // Equality

        public bool Equals(OwnHistoryState? other)
        {
            if (ReferenceEquals(null, other))
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Host.Equals(other.Host)
                && StepIds.SequenceEqual(other.StepIds);
        }

        public override int GetHashCode() {
            var result = HashCode.Combine(Host.GetHashCode(), StepIds.Count);
            foreach (var modalId in StepIds)
                result = HashCode.Combine(result, modalId);
            return result;
        }
    }
}
