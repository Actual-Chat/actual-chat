using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireChat : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected IChats Chats { get; init; } = null!;
    [Inject] private ILogger<RequireChat> Log { get; init; } = null!;

    [Parameter]
    [EditorRequired]
    public string ChatId { get; set; } = "";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        if (ChatId.IsNullOrEmpty())
            Log.LogWarning("ChatId is null");
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return default;
    }
}
