using ActualChat.Search;

namespace ActualChat.Chat;

public interface IChatMarkupHub : IHasServices
{
    ChatId ChatId { get; }

    IMarkupParser Parser { get; }
    IMentionNamer MentionNamer { get; }
    IChatMentionResolver MentionResolver { get; }
    ISearchProvider<MentionSearchResult> MentionSearchProvider { get; }
    IMarkupFormatter EditorHtmlConverter { get; }
}
