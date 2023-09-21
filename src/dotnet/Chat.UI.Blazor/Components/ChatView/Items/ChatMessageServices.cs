using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public class ChatMessageServices(IServiceProvider services)
{
    public IServiceProvider Services { get; } = services;
    public Session Session { get; } = services.Session();
    public History History { get; } = services.GetRequiredService<History>();
    public IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    public IChats Chats { get; } = services.GetRequiredService<IChats>();
    public ChatUI ChatUI { get; } = services.GetRequiredService<ChatUI>();
    public ChatEditorUI ChatEditorUI { get; } = services.GetRequiredService<ChatEditorUI>();
    public AuthorUI AuthorUI { get; } = services.GetRequiredService<AuthorUI>();
    public SelectionUI SelectionUI { get;  } = services.GetRequiredService<SelectionUI>();
    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory { get; } = services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();
    public TimeZoneConverter TimeZoneConverter { get; } = services.GetRequiredService<TimeZoneConverter>();
    // public ILogger<ChatMessage> Log { get; } = services.GetRequiredService<ILogger<ChatMessage>>();
}
