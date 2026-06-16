namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Centralizes the "people are talking together" signal for a chat so multiple surfaces
/// (chat-list tile, activity panel) read one source instead of each deriving it. Wraps the
/// lightweight <see cref="LiveStreamUI"/> stream-activity primitive.
/// </summary>
public class ChatActivityUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private LiveStreamUI LiveStreamUI => Hub.LiveStreamUI;

    [ComputeMethod]
    public virtual async Task<ChatActivity> Get(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.Value.IsNullOrEmpty())
            return ChatActivity.None;
        var authorIds = await LiveStreamUI.GetStreamingAuthorIds(chatId, cancellationToken).ConfigureAwait(false);
        return authorIds.Length == 0
            ? ChatActivity.None
            : new ChatActivity(true, authorIds.Length);
    }
}

public readonly record struct ChatActivity(bool IsActive, int TalkingCount)
{
    public static readonly ChatActivity None = default;
}
