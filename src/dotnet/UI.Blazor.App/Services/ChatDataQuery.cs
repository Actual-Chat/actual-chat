namespace ActualChat.UI.Blazor.App.Services;

public record ChatDataQuery(long IdTileStart, int Offset, int Limit)
{
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }

    public string Format()
 #pragma warning disable MA0076
        => $"{IdTileStart}@[{Offset}->{Limit}]";
 #pragma warning restore MA0076
}
