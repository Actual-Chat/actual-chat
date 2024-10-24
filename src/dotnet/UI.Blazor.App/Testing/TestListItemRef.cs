namespace ActualChat.UI.Blazor.App.Testing;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class TestListItemRef(int id, int rangeSeed, int? contentSeed) : IVirtualListItem
{
    public int Id { get; } = id;
    public int RangeSeed { get; } = rangeSeed;
    public int? ContentSeed { get; } = contentSeed;

    public string Key { get; } = id.Format();
    public int CountAs { get; init; } = 1;
    public bool IsFirstTimeRendered { get; } = true;
}
