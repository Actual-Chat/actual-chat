using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI service for querying live streaming state.
/// Replaces the old ChatStreamingActivity/IChatStreamingActivity pattern with a simpler API.
/// </summary>
public class LiveStreamUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ILiveAudioStreams LiveAudioStreams => Hub.LiveAudioStreams;
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;

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

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> HasActivity(ChatId chatId, CancellationToken cancellationToken)
    {
        // While the RPC peer is down we stop receiving invalidations, so the last known
        // server value is unreliable - report idle and let the idle timers run.
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return false;

        return await LiveSessions.HasActivity(Session, chatId, cancellationToken).ConfigureAwait(false);
    }
}
