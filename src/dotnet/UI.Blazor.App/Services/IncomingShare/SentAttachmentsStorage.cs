namespace ActualChat.UI.Blazor.App.Services;

public class SentAttachmentsStorage
{
    public ChatId ChatId { get; private set; }
    public string[] Urls { get; private set; } = [];

    public event EventHandler<EventArgs>? AttachmentsStored;

    public void Clear()
    {
        ChatId = ChatId.None;
        Urls = [];
    }

    public void Store(ChatId chatId, string[] urls)
    {
        ChatId = chatId;
        Urls = urls;

        AttachmentsStored?.Invoke(this, EventArgs.Empty);
    }
}
