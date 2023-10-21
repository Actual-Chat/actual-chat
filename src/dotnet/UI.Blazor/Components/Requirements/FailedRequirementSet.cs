namespace ActualChat.UI.Blazor.Components;

public readonly record struct FailedRequirementSet(
    ImmutableDictionary<RequirementComponent, Exception> Items)
{
    public static readonly FailedRequirementSet Empty = new(ImmutableDictionary<RequirementComponent, Exception>.Empty);

    public int Count => Items.Count;
    public IEnumerable<RequirementComponent> Components => Items.Keys;
    public IEnumerable<Exception> Errors => Items.Values;
}
