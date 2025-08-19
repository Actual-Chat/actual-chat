namespace ActualChat.UI.Blazor.App.Services;

public static class TranslationUIExt
{
    public static async Task<Translation?> Get(this TranslationUI translationUI, TextEntryId entryId, string consumerId, CancellationToken cancellationToken = default)
        => await translationUI.Get(TranslationSourceId.New(entryId), consumerId, cancellationToken).ConfigureAwait(false);

    public static async Task<Translation?> Get(this TranslationUI translationUI, ThreadChatId threadChatId, ThreadTranslationIdKind kind, string consumerId, CancellationToken cancellationToken = default)
        => await translationUI.Get(TranslationSourceId.New(threadChatId, kind), consumerId, cancellationToken).ConfigureAwait(false);

    public static async Task<Translation?> Get(this TranslationUI translationUI, ConversationId conversationId, ConversationTranslationIdKind kind, string consumerId, CancellationToken cancellationToken = default)
        => await translationUI.Get(TranslationSourceId.New(conversationId, kind), consumerId, cancellationToken).ConfigureAwait(false);

    public static async Task<Translation?> GetExisting(this TranslationUI translationUI, TextEntryId entryId, CancellationToken cancellationToken = default)
        => await translationUI.GetExisting(TranslationSourceId.New(entryId), cancellationToken).ConfigureAwait(false);

    public static async Task<Translation?> GetExisting(this TranslationUI translationUI, ThreadChatId threadChatId, ThreadTranslationIdKind kind, CancellationToken cancellationToken = default)
        => await translationUI.GetExisting(TranslationSourceId.New(threadChatId, kind), cancellationToken).ConfigureAwait(false);

    public static async Task<Translation?> GetExisting(this TranslationUI translationUI, ConversationId conversationId, ConversationTranslationIdKind kind, CancellationToken cancellationToken = default)
        => await translationUI.GetExisting(TranslationSourceId.New(conversationId, kind), cancellationToken).ConfigureAwait(false);
}
