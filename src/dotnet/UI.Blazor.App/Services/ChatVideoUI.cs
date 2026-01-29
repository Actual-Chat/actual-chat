using ActualChat.Streaming;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Provides reactive access to video stream data for the current chat.
/// Player lifecycle management is handled by VideoTrackPlayer components.
/// </summary>
public partial class ChatVideoUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IRealtimeStreaming RealtimeStreaming => Hub.Services.GetRequiredService<IRealtimeStreaming>();

    [ComputeMethod]
    public virtual async Task<ActiveVideoStreams?> GetActiveVideoStreams(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return null;

        return await RealtimeStreaming
            .GetActiveVideoStreams(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<AuthorId[]> GetVideoStreamingAuthorIds(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return [];

        return await RealtimeStreaming
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsAnyoneVideoStreaming(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return false;

        var authorIds = await RealtimeStreaming
            .GetVideoStreamingAuthorIds(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
        return authorIds.Length > 0;
    }

    [ComputeMethod]
    public virtual async Task<int> GetVideoStreamMemberCount(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        if (chatId is null)
            return 0;

        return await RealtimeStreaming
            .GetVideoStreamMemberCount(Session, chatId, cancellationToken)
            .ConfigureAwait(false);
    }
}
