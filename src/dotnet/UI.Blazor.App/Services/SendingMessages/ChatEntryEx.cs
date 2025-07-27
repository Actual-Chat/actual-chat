namespace ActualChat.UI.Blazor.App.Services;

public static class ChatEntryEx
{
    public static string GetClientId(this ChatEntry chatEntry)
    {
        if (!chatEntry.IsSending)
            return chatEntry.Id.Value;

        return !chatEntry.IsSending ? chatEntry.Id.Value : chatEntry.ClientUid;
    }

    public static SendingMessage? GetSendingMessage(this ChatEntry chatEntry)
        => chatEntry.SendingTag as SendingMessage;
}
