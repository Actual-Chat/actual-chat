namespace ActualChat.UI.Blazor.App.Components;

public class SentAttachmentsStorage
{
    private AttachFileInfo[] _fileInfos = [];

    public ChatId? ChatId { get; private set; }
    public bool HasFiles => _fileInfos.Length > 0;

    public event EventHandler<EventArgs>? AttachmentsStored;

    public AttachFileInfo[] Pop()
    {
        var fileInfos = _fileInfos;
        ChatId = null;
        _fileInfos = [];
        return fileInfos;
    }

    public void Push(ChatId chatId, AttachFileInfo[] fileInfos)
    {
        ChatId = chatId;
        _fileInfos = fileInfos;

        AttachmentsStored?.Invoke(this, EventArgs.Empty);
    }
}
