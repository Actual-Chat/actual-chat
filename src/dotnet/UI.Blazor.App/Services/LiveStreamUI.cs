using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI service for querying live streaming state.
/// Replaces the old ChatStreamingActivity/IChatStreamingActivity pattern with a simpler API.
/// </summary>
public class LiveStreamUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private readonly ConcurrentDictionary<ChatId, Moment> _lastActivityTimes = new();

    private ILiveAudioStreams LiveAudioStreams => Hub.LiveAudioStreams;
    private MomentClock ServerClock => Clocks.ServerClock;

    [ComputeMethod]
    public virtual async Task<AuthorId[]> GetStreamingAuthorIds(ChatId chatId, CancellationToken cancellationToken)
    {
        var streams = await LiveAudioStreams.List(Session, chatId, cancellationToken).ConfigureAwait(false);
        return streams.Select(s => s.AuthorId).Distinct().ToArray();
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsAuthorStreaming(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var authorIds = await GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Contains(authorId);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsAnyoneStreaming(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Length > 0;
    }

    [ComputeMethod(ConsolidationDelay = 0.5)] // ConsolidationDelay ensures it gets auto-recomputed
    public virtual async Task<Moment?> GetLastActivityServerTime(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        if (authorIds.Length == 0)
            return _lastActivityTimes.GetOrAdd(chatId, static (_, self) => self.ServerClock.Now, this);

        _lastActivityTimes.TryRemove(chatId, out _);
        return null;
    }
}
