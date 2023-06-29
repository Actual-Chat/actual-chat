using ActualChat.Kvas;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class KvasExt
{
    public static ValueTask<RelatedChatEntry?> GetDraftRelatedEntry(this IKvas kvas, ChatId chatId, CancellationToken cancellationToken = default)
        => kvas.Get<RelatedChatEntry?>(GetDraftRelatedEntryKey(chatId), cancellationToken);

    public static Task SetDraftRelatedEntry(this IKvas kvas, ChatId chatId, RelatedChatEntry? relatedChatEntry)
        => kvas.Set(GetDraftRelatedEntryKey(chatId), relatedChatEntry);

    private static string GetDraftRelatedEntryKey(ChatId chatId)
        => $"MessageDraft.{chatId}.RelatedEntry";
}
