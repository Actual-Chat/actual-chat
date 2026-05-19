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
            var picture = c.Picture ?? new Picture(null, null, c.PrimaryName);
            // Suffix the candidate name with the place title only when the candidate
            // lives in a different place than the host chat (and isn't the place mention
            // itself — those carry PlaceId == Id.Target).
            var isPlaceMention = c.Id.Target is PlaceId;
            var suffix = c.PlaceId is { } cp && cp != hostPlaceId && !isPlaceMention
                ? c.PlaceTitle
                : null;
            var isInHostPlace = hostPlaceId is not null && c.PlaceId == hostPlaceId && !isPlaceMention;
            result[i] = new MentionSearchResult(c.Id, searchPhrase.GetMatch(c.PrimaryName), picture) {
                IsChatMember = c.IsChatMember,
                PlaceTitleSuffix = suffix,
                IsInHostPlace = isInHostPlace,
            };
        }
        return result;
    }
}
