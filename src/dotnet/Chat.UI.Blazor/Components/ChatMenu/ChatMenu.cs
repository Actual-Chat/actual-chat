using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class ChatMenu : ComputedMenuBase<ChatInfo, ChatMenuContent>
{
    [Inject] private ChatUI ChatUI { get; init; } = null!;

    protected override Task<ChatInfo?> ComputeState(CancellationToken cancellationToken)
        => ChatUI.Get(new ChatId(Arguments[0]), cancellationToken);
}
