namespace ActualChat.UI.Blazor.Components;

public class FailedRequirementSet
{
    public static FailedRequirementSet Empty { get; } = new();

    public ImmutableDictionary<RequirementComponent, Exception> Items { get; }
    public int Count => Items.Count;
    public IEnumerable<RequirementComponent> Components => Items.Keys;
    public IEnumerable<Exception> Errors => Items.Values;

    public FailedRequirementSet()
        : this(ImmutableDictionary<RequirementComponent, Exception>.Empty) { }
    public FailedRequirementSet(ImmutableDictionary<RequirementComponent, Exception> items)
        => Items = items;
}
