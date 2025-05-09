namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialAuthor
{
    public static readonly AuthorFull None = new (null!, null!, 0);
    public static readonly AuthorFull Loading = new (null!, null!, -1);
}
