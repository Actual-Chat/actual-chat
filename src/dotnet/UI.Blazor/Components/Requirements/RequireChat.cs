using ActualChat.Chat;

namespace ActualChat.UI.Blazor.Components;

public class RequireChat : RequirementComponent
{
    [Inject] protected Session Session { get; init; } = null!;
    [Inject] protected IChats Chats { get; init; } = null!;
    [Inject] protected ILogger<RequireChat> Log { get; init; } = null!;

    [Parameter, EditorRequired] public string ChatSid { get; set; } = "";

    public override string ToString()
        => $"{GetType().GetName()}(ChatSid = {ChatSid})";

    public override async Task<Unit> Require(CancellationToken cancellationToken)
    {
        if (!ChatId.TryParse(ChatSid, out var chatId)) {
            Log.LogWarning("Invalid ChatId");
            throw StandardError.Format<ChatId>();
        }

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
        return default;
    }
}
