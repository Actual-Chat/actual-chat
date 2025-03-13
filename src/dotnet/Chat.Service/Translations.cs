namespace ActualChat.Chat;

public class Translations(IServiceProvider services) : ITranslations
{
    [field: AllowNull, MaybeNull]
    private ITranslationsBackend Backend => field ??= services.GetRequiredService<ITranslationsBackend>();
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    private IChatEntryLanguagesBackend ChatEntryLanguagesBackend => field ??= services.GetRequiredService<IChatEntryLanguagesBackend>();

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(Session session, TranslationId id, CancellationToken cancellationToken)
    {
        _ = await Chats.Get(session, id.ChatEntryId.ChatId, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.Get(id, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual Task<ChatEntryLanguage?> GetLanguage(Session session, ChatEntryId id, CancellationToken cancellationToken)
        => ChatEntryLanguagesBackend.GetLanguage(id, cancellationToken);
}
