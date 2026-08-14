using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// UI service for querying live streaming state.
/// Replaces the old ChatStreamingActivity/IChatStreamingActivity pattern with a simpler API.
/// </summary>
public class LiveStreamUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ILiveSessions LiveSessions => Hub.LiveSessions;
    private ConnectivityUI ConnectivityUI => Hub.ConnectivityUI;

    [ComputeMethod]
    public virtual Task<ApiArray<AuthorId>> GetAudioStreamingAuthorIds(
        ChatId chatId, CancellationToken cancellationToken)
        // Pass-through: the aggregation and its consolidation live on the server, so this hands
        // back the very instance the RPC layer produced and adds no churn of its own.
        => LiveSessions.GetAudioStreamingAuthorIds(Session, chatId, cancellationToken);

    [ComputeMethod]
    public virtual async Task<ConversationStats?> GetConversationStats(
        ChatId chatId, CancellationToken cancellationToken)
    {
        // While the RPC peer is down we stop receiving invalidations, so the last known server
        // value is unreliable - report "no live conversation", same as HasActivity reports idle.
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return null;

        return await LiveSessions.GetConversationStats(Session, chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsAuthorStreamingAudio(
        ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        var authorIds = await GetAudioStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Contains(authorId);
    }

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> IsAnyoneStreamingAudio(ChatId chatId, CancellationToken cancellationToken)
    {
        var authorIds = await GetAudioStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Count > 0;
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

    [ComputeMethod(ConsolidationDelay = 0.2)]
    public virtual async Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken)
    {
        // Same connectivity gate as HasActivity, for the same reason.
        var isConnected = await ConnectivityUI.IsConnected.Use(cancellationToken).ConfigureAwait(false);
        if (!isConnected)
            return false;

        return await LiveSessions.HasRecorder(Session, chatId, cancellationToken).ConfigureAwait(false);
    }
}
