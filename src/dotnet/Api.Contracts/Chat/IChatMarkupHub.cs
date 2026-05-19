using ActualChat.Search;

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
    ISearchProvider<MentionSearchResult> MentionSearchProvider { get; }
    IMarkupFormatter EditorHtmlConverter { get; }
}
