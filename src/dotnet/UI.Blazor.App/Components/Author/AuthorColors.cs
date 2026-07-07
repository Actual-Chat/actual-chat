namespace ActualChat.UI.Blazor.App.Components;

public static class AuthorColors
{
    public const int Count = 10;

    public static int GetIndex(AuthorId? authorId)
    {
        if (authorId is null || authorId.Value.IsNullOrEmpty())
            return 0;
        var offset = authorId.ChatId.Value.GetXxHash3();
        return (int)((((authorId.LocalId + offset) % Count) + Count) % Count);
    }

    public static string GetColorClass(AuthorId? authorId, bool isOwn = false)
    {
        if (authorId is null || authorId.Value.IsNullOrEmpty())
            return "";
        if (authorId.ChatId.Kind == ChatKind.Peer)
            return isOwn ? "author-color-self" : "author-color-peer";
        return $"author-color-{GetIndex(authorId) + 1}";
    }
}
