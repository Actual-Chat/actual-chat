namespace ActualChat.Chat.UI.Blazor.Testing;

public sealed class TestListItemRef : IVirtualListItem
{
    public int Id { get; }
    public int RangeSeed { get; }
    public int? ContentSeed { get; }

    public Symbol Key { get; }
    public int CountAs { get; init; } = 1;

    public TestListItemRef(int id, int rangeSeed, int? contentSeed)
    {
        Id = id;
        Key = id.ToString(CultureInfo.InvariantCulture);
        RangeSeed = rangeSeed;
        ContentSeed = contentSeed;
    }
}
