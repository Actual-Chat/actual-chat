namespace ActualChat.UI.Blazor.App.Testing;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class TestListItemRef(int id, int rangeSeed, int? contentSeed) : IVirtualListItem
{
    public int Id { get; } = id;
    public int RangeSeed { get; } = rangeSeed;
    public int? ContentSeed { get; } = contentSeed;
    public string Key { get; } = id.Format();
    public bool IsGroup => false;
    public bool ShouldSkipKey => false;
    public bool IsFirstTimeRendered { get; } = true;
    public bool MustAnimateHeightChanges { get; init; } = true;
    public int? HeightSettleDelayMs { get; init; }
}
