namespace ActualChat.Chat;

/// <summary>
/// Provides markup parsing and mention resolution services for a chat.
/// </summary>
public interface IChatMarkupHub : IHasServices
{
    ChatId ChatId { get; }

    IMarkupParser Parser { get; }
    IMentionResolver MentionResolver { get; }
    IChatMentionResolver ChatMentionResolver { get; }
    IMarkupFormatter EditorHtmlConverter { get; }
    SystemEntryMarkupBuilder SystemEntryMarkupBuilder { get; }
    EmptyEntryMarkupBuilder EmptyEntryMarkupBuilder { get; }
}
