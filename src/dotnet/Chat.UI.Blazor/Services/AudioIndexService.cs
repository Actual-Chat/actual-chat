namespace ActualChat.Chat.UI.Blazor.Services;

public class AudioIndexService
{
    private readonly IChatService _chatService;
    private readonly ILogger<AudioIndexService> _log;

    public AudioIndexService(IChatService chatService, ILogger<AudioIndexService> log)
    {
        _chatService = chatService;
        _log = log;
    }

    [ComputeMethod]
    public virtual async Task<ChatEntry?> FindAudioEntry(
        Session session,
        ChatEntry entry,
        TimeSpan offset,
        CancellationToken cancellationToken)
    {
        try {
            var entryStart = entry.BeginsAt.ToDateTime().Add(offset).ToMoment();
            var startId = entry.Id - 64;
            var endId = entry.Id + 64;
            var idLogCover = ChatConstants.IdTiles;
            var ranges = idLogCover.GetTileCover((startId, endId));
            var entryLists = await Task
                .WhenAll(ranges.Select(r => _chatService.GetEntries(session, entry.ChatId, r, cancellationToken)))
                .ConfigureAwait(false);
            var chatEntries = entryLists
                .SelectMany(entries => entries)
                .Where(c => c.ContentType == ChatContentType.Audio
                    && c.AuthorId == entry.AuthorId
                    && c.BeginsAt <= entryStart
                    && (c.EndsAt == null || c.EndsAt > entryStart))
                .OrderBy(c => c.BeginsAt)
                .ThenByDescending(c => c.EndsAt ?? Moment.MinValue)
                .ToList();
            return chatEntries.Count == 0
                ? null
                : chatEntries[0];
        }
        catch (Exception e) {
            _log.LogError(e, "Error finding Audio chat entry");
            throw;
        }
    }
}
