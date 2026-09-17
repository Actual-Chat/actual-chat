namespace ActualChat.UI.Blazor.Services;

public sealed partial class DebugUI
{
    [JSInvokable]
    public Task ChatMaintenance(string chatId, bool isEnabled)
        => Hub.Commander.Call(new Chats_SetMaintenance {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            IsEnabled = isEnabled,
        });
}
