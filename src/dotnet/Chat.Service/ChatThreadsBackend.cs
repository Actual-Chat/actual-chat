namespace ActualChat.Chat;

public class ChatThreadsBackend(IServiceProvider services) : IChatThreadsBackend
{
    [field: AllowNull, MaybeNull]
    private IRolesBackend RolesBackend => field ??= services.GetRequiredService<IRolesBackend>();
    [field: AllowNull, MaybeNull]
    private IAuthorsBackend AuthorsBackend => field ??= services.GetRequiredService<IAuthorsBackend>();

    // [ComputeMethod]
    public virtual async Task<AuthorFull?> GetThreadCreator(ChatId chatId, CancellationToken cancellationToken)
    {
        if (!chatId.IsThread)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var ownerAuthorIds = await RolesBackend.ListAuthorIds(chatId, ownerRole.Id, cancellationToken).ConfigureAwait(false);
        if (ownerAuthorIds.Length <= 0)
            return null;

        var ownerAuthorId = ActualChat.Chat.AuthorsBackend.Remap(ownerAuthorIds[0], chatId);
        var ownerAuthor = await AuthorsBackend.Get(chatId, ownerAuthorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
        return ownerAuthor;
    }
}
