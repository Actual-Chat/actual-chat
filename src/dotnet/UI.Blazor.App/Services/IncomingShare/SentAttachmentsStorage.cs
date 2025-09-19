namespace ActualChat.UI.Blazor.App.Services;

public class SentAttachmentsStorage
{
    public ChatId? ChatId { get; private set; }
    public AttachFileInfo[] FileInfos { get; private set; } = [];

    public event EventHandler<EventArgs>? AttachmentsStored;

    public void Clear()
    {
        ChatId = null;
        FileInfos = [];
    }

    public void Store(ChatId chatId, AttachFileInfo[] fileInfos)
    {
        ChatId = chatId;
        FileInfos = fileInfos;

        AttachmentsStored?.Invoke(this, EventArgs.Empty);
    }
}
