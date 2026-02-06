using ActualChat.Search;

namespace ActualChat.Chat;

/// <summary>
/// Provides markup parsing and mention resolution services for a chat.
/// </summary>
public interface IChatMarkupHub : IHasServices
{
    ChatId ChatId { get; }

    IMarkupParser Parser { get; }
    IMentionNamer MentionNamer { get; }
    IChatMentionResolver MentionResolver { get; }
    ISearchProvider<MentionSearchResult> MentionSearchProvider { get; }
    IMarkupFormatter EditorHtmlConverter { get; }
}
