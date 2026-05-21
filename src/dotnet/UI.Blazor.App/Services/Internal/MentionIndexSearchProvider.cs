using ActualChat.Search;

namespace ActualChat.UI.Blazor.App.Services.Internal;

internal class MentionIndexSearchProvider(IServiceProvider services, ChatId chatId)
    : ISearchProvider<MentionSearchResult>
{
    private MentionIndexUI Index { get; } = services.GetRequiredService<MentionIndexUI>();

    public ChatId ChatId { get; } = chatId;

    public Task<MentionSearchResult[]> Find(string filter, int limit, CancellationToken cancellationToken)
        => Find(filter, MentionKindFilter.All, limit, cancellationToken);

    public async Task<MentionSearchResult[]> Find(
        string filter, MentionKindFilter kindFilter, int limit, CancellationToken cancellationToken)
    {
        var candidates = await Index
            .Find(ChatId, filter, kindFilter, limit, cancellationToken)
            .ConfigureAwait(false);
        var searchPhrase = filter.ToSearchPhrase(true, true);
        var hostPlaceId = (ChatId as PlaceChatId)?.PlaceId;
        var result = new MentionSearchResult[candidates.Length];
        for (var i = 0; i < candidates.Length; i++) {
            var c = candidates[i];
            var picture = c.Picture ?? new Picture(null, null, c.Title);
            var (description, mentionName) = Describe(c, hostPlaceId);
            result[i] = new MentionSearchResult(c.Id, searchPhrase.GetMatch(c.Title), picture) {
                IsChatMember = c.IsChatMember,
                Description = description,
                MentionName = mentionName,
            };
        }
        return result;
    }

    // Computes the picker description suffix and the name baked into the mention on insert.
    private static (string? Description, string MentionName) Describe(MentionCandidate c, PlaceId? hostPlaceId)
    {
        switch (c.Id.TargetId) {
        case EmojiRef:
            return ("emoji", c.Title);
        case ActualChat.PlaceId:
            return ("place", c.Title);
        case ActualChat.UserId or ActualChat.AuthorId:
            return (c.IsChatMember ? null : "- not in this chat", c.Title);
        case ActualChat.ChatId:
            if (c.PlaceId is not { } placeId)
                return (null, c.Title); // standalone group chat
            if (placeId == hostPlaceId)
                return ("in this place", c.Title);
            var placeTitle = c.PlaceTitle.NullIfEmpty();
            return placeTitle is null
                ? ("in another place", c.Title)
                : ($"in {placeTitle}", $"{placeTitle} › {c.Title}");
        default:
            return (null, c.Title);
        }
    }
}
