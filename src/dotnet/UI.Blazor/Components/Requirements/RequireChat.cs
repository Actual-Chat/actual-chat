using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Components;

public class RequireChat : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IAccounts Accounts { get; init; } = null!;
    [Inject] protected IChats Chats { get; init; } = null!;
    [Inject] protected ILogger<RequireChat> Log { get; init; } = null!;

    [Parameter, EditorRequired]
    public string Id { get; set; } = "";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        if (!ChatId.TryParse(Id, out var chatId)) {
            Log.LogWarning("Invalid ChatId");
            throw StandardError.Format<ChatId>();
        }
        var chat = await Chats.Get(Session, chatId.Value, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return default;
    }
}
