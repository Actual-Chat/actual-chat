using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireChat : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected IChats Chats { get; init; } = null!;

    [Parameter] public string ChatId { get; set; } = "";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return default;
    }
}
