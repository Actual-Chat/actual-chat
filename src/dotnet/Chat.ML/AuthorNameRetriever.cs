namespace ActualChat.Chat.ML;

internal interface IAuthorNameRetriever
{
    Task<string> GetAuthorName(AuthorId authorId);
}

internal sealed class DefaultAuthorNameRetriever(IAuthorsBackend authorsBackend) : IAuthorNameRetriever
{
    public async Task<string> GetAuthorName(AuthorId authorId)
    {
        var author = await authorsBackend
            .Get(authorId.ChatId, authorId, AuthorsBackend_GetAuthorOption.Full, default)
            .ConfigureAwait(false);
        var authorName = author?.Avatar.Name ?? "author-" + authorId.LocalId;
        return authorName;
    }
}

internal sealed class DelegateAuthorNameRetriever(Func<AuthorId, Task<string>> func) : IAuthorNameRetriever
{
    public  Task<string> GetAuthorName(AuthorId authorId)
        => func(authorId);
}
