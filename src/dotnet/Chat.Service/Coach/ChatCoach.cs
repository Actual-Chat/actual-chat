using ActualChat.Chat.Module;

namespace ActualChat.Chat.Coach;

public class ChatCoach(IServiceProvider services) : IChatCoach
{
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private ICoachAnalysisBackend Backend { get; } = services.GetRequiredService<ICoachAnalysisBackend>();

    // [ComputeMethod]
    public virtual Task<bool> IsEnabled(Session session, CancellationToken cancellationToken)
        => Task.FromResult(Settings.Coach.IsEnabled);

    // [ComputeMethod]
    public virtual async Task<ApiArray<CoachEntryMarks>> GetOwnMarks(
        Session session, ChatId chatId, Range<long> lidRange, CancellationToken cancellationToken)
    {
        if (!Settings.Coach.IsEnabled)
            return ApiArray<CoachEntryMarks>.Empty;

        var author = await Authors.GetOwn(session, chatId, cancellationToken).ConfigureAwait(false);
        if (author is null)
            return ApiArray<CoachEntryMarks>.Empty;

        // The backend caches and invalidates per entry tile, so the client range is served tile by tile
        var marks = new List<CoachEntryMarks>();
        foreach (var tile in Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange)) {
            var tileMarks = await Backend.ListMarks(chatId, author.Id, tile.Range, cancellationToken)
                .ConfigureAwait(false);
            marks.AddRange(tileMarks.Where(m => m.EntryLid >= lidRange.Start && m.EntryLid < lidRange.End));
        }
        return marks.ToApiArray();
    }
}
