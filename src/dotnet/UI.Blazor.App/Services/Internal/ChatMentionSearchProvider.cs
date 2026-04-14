using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services.Internal;

internal class ChatMentionSearchProvider(IServiceProvider services, ChatId chatId)
    : ISearchProvider<MentionSearchResult>
{
    private MentionUI MentionUI { get; } = services.GetRequiredService<MentionUI>();

    public ChatId ChatId { get; } = chatId;

    public Task<MentionSearchResult[]> Find(string filter, int limit, CancellationToken cancellationToken)
        => MentionUI.Find(ChatId, filter, limit, cancellationToken);
}
