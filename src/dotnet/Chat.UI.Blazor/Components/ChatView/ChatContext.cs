using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatContext(IServiceProvider services, ChatId chatId) : IHasServices, IEquatable<ChatContext>
{
    private Session? _session;
    private History? _history;
    private IAuthors? _authors;
    private IChats? _chats;
    private ChatUI? _chatUI;
    private AuthorUI? _authorUI;
    private SelectionUI? _selectionUI;
    private ChatEditorUI? _chatEditorUI;
    private ShareUI? _shareUI;
    private TimeZoneConverter? _timeZoneConverter;
    private UrlMapper? _urlMapper;
    private KeyedFactory<IChatMarkupHub, ChatId>? _chatMarkupHubFactory;

    public IServiceProvider Services { get; } = services;
    public ChatId ChatId { get; } = chatId;

    public Session Session => _session ??= Services.Session();
    public History History => _history ??= Services.GetRequiredService<History>();
    public IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    public IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    public ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    public AuthorUI AuthorUI => _authorUI ??= Services.GetRequiredService<AuthorUI>();
    public SelectionUI SelectionUI => _selectionUI ??= Services.GetRequiredService<SelectionUI>();
    public ChatEditorUI ChatEditorUI => _chatEditorUI ??= Services.GetRequiredService<ChatEditorUI>();
    public ShareUI ShareUI => _shareUI ??= Services.GetRequiredService<ShareUI>();
    public ModalUI ModalUI => ShareUI.ModalUI;
    public TimeZoneConverter TimeZoneConverter => _timeZoneConverter ??= Services.GetRequiredService<TimeZoneConverter>();
    public UrlMapper UrlMapper => _urlMapper ??= Services.GetRequiredService<UrlMapper>();
    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => _chatMarkupHubFactory ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();

    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);

    // Equality

    public bool Equals(ChatContext? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return ReferenceEquals(Services, other.Services) && ChatId.Equals(other.ChatId);
    }

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatContext other && Equals(other));

    public override int GetHashCode() => HashCode.Combine(Services, ChatId);
    public static bool operator ==(ChatContext? left, ChatContext? right) => Equals(left, right);
    public static bool operator !=(ChatContext? left, ChatContext? right) => !Equals(left, right);
}
