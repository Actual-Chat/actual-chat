using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.App.Services;

public static class KvasExt
{
    public static ValueTask<RelatedEntryRef?> GetDraftRelatedEntryRef(
        this IKvas kvas, ChatId chatId, CancellationToken cancellationToken = default)
        => kvas.Get<RelatedEntryRef?>(GetDraftRelatedEntryKey(chatId), cancellationToken);

    public static Task SetDraftRelatedEntryRef(
        this IKvas kvas, ChatId chatId, RelatedEntryRef? relatedChatEntry)
        => kvas.Set(GetDraftRelatedEntryKey(chatId), relatedChatEntry);

    // Private methods

    private static string GetDraftRelatedEntryKey(ChatId chatId)
        => $"MessageDraft.{chatId}.RelatedEntry";
}
