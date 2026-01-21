namespace ActualChat.UI.Blazor.App.Services;

public static class ChatEntryEx
{
    private static readonly ILogger Log = StaticLog.For(typeof(ChatEntryEx));

    public static string GetClientId(this ChatEntry chatEntry, bool isOwnMessage)
    {
        if (chatEntry.IsSystemEntry)
            return chatEntry.Id.Value;

        var useClientId = chatEntry.IsSending || chatEntry.HasAttachmentUploads && isOwnMessage;
        if (useClientId)
            return chatEntry.ClientUid;

        return chatEntry.Id.Value;
    }

    public static SendingMessage? GetSendingMessage(this ChatEntry chatEntry)
        => chatEntry.SendingTag as SendingMessage;
}
