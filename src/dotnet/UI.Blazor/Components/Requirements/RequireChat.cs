using ActualChat.Chat;

namespace ActualChat.UI.Blazor.Components;

public sealed class RequireChat : RequirementComponent
{
    [field: AllowNull, MaybeNull]
    private IChats Chats => field ??= Hub.GetRequiredService<IChats>();

    [Parameter, EditorRequired] public string ChatSid { get; set; } = "";

    public override string ToString()
        => $"{GetType().GetName()}(ChatId = {ChatSid})";

    public override async Task Require(CancellationToken cancellationToken)
    {
        if (!ChatId.TryParse(ChatSid, out var chatId)) {
            Log.LogWarning("Invalid ChatId");
            throw StandardError.Format<ChatId>();
        }

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        chat.Require();
    }
}
