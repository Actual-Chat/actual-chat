namespace ActualChat.UI.Blazor.App.Components;

public class SentContentStorage
{
    private string _text = "";
    private AttachFileInfo[] _fileInfos = [];

    public ChatId? ChatId { get; private set; }

    public event EventHandler<EventArgs>? ContentStored;

    public (string Text, AttachFileInfo[] Files) Pop()
    {
        var text = _text;
        var fileInfos = _fileInfos;
        _text = "";
        _fileInfos = [];
        ChatId = null;
        return (text, fileInfos);
    }

    public void Push(ChatId chatId, AttachFileInfo[] fileInfos, string text = "")
    {
        ChatId = chatId;
        _fileInfos = fileInfos;
        _text = text;
        ContentStored?.Invoke(this, EventArgs.Empty);
    }
}
