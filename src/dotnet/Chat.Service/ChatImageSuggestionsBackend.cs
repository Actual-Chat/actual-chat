using ActualChat.Chat.ML;
using ActualChat.Chat.Module;

namespace ActualChat.Chat;

public class ChatImageSuggestionsBackend(IServiceProvider services) : IChatImageSuggestionsBackend
{
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;

    private IServiceProvider Services { get; } = services;
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IChatImageDescriber ChatImageDescriber => field ??= Services.GetRequiredService<IChatImageDescriber>();

    // No "is there enough to describe this" check: the client decides when to ask, and an explicit
    // regenerate must work on a chat of any size. The describer returning "" is still a no.
    public async Task<string> DescribeChat(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return "";

        var entries = await ListRecentTextEntries(chatId, cancellationToken).ConfigureAwait(false);
        return await ChatImageDescriber
            .Describe(chat.Title, chat.Description, entries, cancellationToken)
            .ConfigureAwait(false);
    }

    // chatIds comes from the caller, which picked it under the user's own read permissions
    public async Task<string> DescribePlace(
        PlaceId placeId,
        ApiArray<ChatId> chatIds,
        bool isBackground,
        CancellationToken cancellationToken)
    {
        var place = await PlacesBackend.Get(placeId, cancellationToken).ConfigureAwait(false);
        if (place is null)
            return "";

        var sources = new List<ChatImageDescriptionSource>();
        foreach (var chatId in chatIds) {
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                continue;

            var entries = await ListFirstTextEntries(chatId, cancellationToken).ConfigureAwait(false);
            sources.Add(new ChatImageDescriptionSource(chat.Title, chat.Description, entries));
        }
        return await ChatImageDescriber
            .DescribePlace(place, sources, isBackground, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task<IReadOnlyCollection<ChatEntrySlim>> ListFirstTextEntries(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var range = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var entries = new List<ChatEntrySlim>();
        var nextId = range.Start;
        while (nextId < range.End && entries.Count < 10) {
            var tileRange = EntryIdTiles.GetTile(nextId).Range;
            var tile = await ChatsBackend.GetTile(chatId, tileRange, false, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries.OrderBy(x => x.LocalId)) {
                if (entry.LocalId < nextId || !range.Contains(entry.LocalId)
                    || entry.IsSystemEntry || entry.IsRemoved || entry.IsContentStreaming
                    || entry.Content.IsNullOrWhiteSpace())
                    continue;

                entries.Add(new ChatEntrySlim(entry));
                if (entries.Count == 10)
                    break;
            }
            nextId = tileRange.End;
        }
        return entries;
    }

    private async Task<IReadOnlyList<ChatEntrySlim>> ListRecentTextEntries(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var maxCount = Settings.MaxImageSuggestionEntries;
        var start = Math.Max(lidRange.Start, lidRange.End - maxCount);
        var tailRange = new Range<long>(start, lidRange.End);
        if (tailRange.IsEmpty)
            return [];

        var tiles = await EntryIdTiles
            .GetCoveringTiles(tailRange)
            .Select(idTile => ChatsBackend.GetTile(chatId, idTile.Range, false, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        return tiles
            .SelectMany(tile => tile.Entries)
            .Where(e => tailRange.Contains(e.LocalId))
            // A system entry stores no text, so it would reach the describer as a blank line
            .Where(e => !e.IsSystemEntry && !e.IsRemoved && !e.IsContentStreaming)
            .DistinctBy(e => e.LocalId)
            .OrderBy(e => e.LocalId)
            .Select(e => new ChatEntrySlim(e))
            .ToList();
    }
}
