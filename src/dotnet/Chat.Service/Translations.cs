namespace ActualChat.Chat;

/// <summary>
/// Frontend service for retrieving chat entry translations.
/// </summary>
public class Translations(IServiceProvider services) : ITranslations
{
    private ITranslationsBackend Backend => field ??= services.GetRequiredService<ITranslationsBackend>();
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    private IChatEntryLanguagesBackend ChatEntryLanguagesBackend => field ??= services.GetRequiredService<IChatEntryLanguagesBackend>();

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(
        Session session,
        TranslationId id,
        bool translateIfMissing,
        CancellationToken cancellationToken)
    {
        _ = await Chats.Get(session, id.SourceId.ChatId, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.Get(id, translateIfMissing, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatLanguageTile> GetLanguageTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken)
    {
        _ = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        return await ChatEntryLanguagesBackend.GetTile(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }
}
