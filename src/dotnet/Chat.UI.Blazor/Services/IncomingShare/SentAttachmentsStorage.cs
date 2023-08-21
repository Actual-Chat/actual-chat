namespace ActualChat.Chat.UI.Blazor.Services;

public class SentAttachmentsStorage
{
    public ChatId ChatId { get; private set; }
    public string[] Urls { get; private set; } = Array.Empty<string>();

    public void Clear()
    {
        ChatId = ChatId.None;
        Urls = Array.Empty<string>();
    }

    public void Store(ChatId chatId, string[] urls)
    {
        ChatId = chatId;
        Urls = urls;
    }
}
