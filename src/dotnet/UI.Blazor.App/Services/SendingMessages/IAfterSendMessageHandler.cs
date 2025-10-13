namespace ActualChat.UI.Blazor.App.Services;

public interface IAfterSendMessageHandler
{
    void Invoke(string args, Result<ChatEntry?> chatEntry);
}
