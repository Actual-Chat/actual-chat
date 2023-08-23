namespace ActualChat.Chat.UI.Blazor.Services;

public class SentAttachmentsStorage
{
    public ChatId ChatId { get; private set; }
    public string[] Urls { get; private set; } = Array.Empty<string>();

    public event EventHandler<EventArgs>? AttachmentsStored;

    public void Clear()
    {
        ChatId = ChatId.None;
        Urls = Array.Empty<string>();
    }

    public void Store(ChatId chatId, string[] urls)
    {
        ChatId = chatId;
        Urls = urls;

        AttachmentsStored?.Invoke(this, EventArgs.Empty);
    }
}
