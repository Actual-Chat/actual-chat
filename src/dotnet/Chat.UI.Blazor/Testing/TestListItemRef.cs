namespace ActualChat.Chat.UI.Blazor.Testing;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class TestListItemRef : IVirtualListItem
{
    public int Id { get; }
    public int RangeSeed { get; }
    public int? ContentSeed { get; }

    public Symbol Key { get; }
    public int CountAs { get; init; } = 1;
    public bool IsNew { get; } = true;

    public TestListItemRef(int id, int rangeSeed, int? contentSeed)
    {
        Id = id;
        Key = id.Format();
        RangeSeed = rangeSeed;
        ContentSeed = contentSeed;
    }
}
