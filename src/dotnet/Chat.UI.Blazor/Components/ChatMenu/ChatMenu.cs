using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class ChatMenu : ComputedMenuBase<ChatState, ChatMenuContent>
{
    [Inject] private ChatUI ChatUI { get; init; } = null!;

    protected override Task<ChatState?> ComputeState(CancellationToken cancellationToken)
        => ChatUI.GetState(new ChatId(Arguments[0]), false, cancellationToken);
}
