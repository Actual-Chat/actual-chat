using ActualChat.Contacts;
using ActualChat.Pooling;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages : WorkerBase
{
    private const int MaxUnreadChatCount = 99;
    private readonly Dictionary<Symbol, SharedResourcePool<Symbol, ChatUnreadMessages>.Lease> _leases = new (); // caching leases to prevent UnreadMessages recreation
    private readonly SharedResourcePool<Symbol, ChatUnreadMessages> _pool;

    private Session Session { get; }
    private IContacts Contacts { get; }
    private IChats Chats { get; }
    private AccountSettings AccountSettings { get; }
    private ChatUnreadMessagesFactory ChatUnreadMessagesFactory { get; }

    public UnreadMessages(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Contacts = services.GetRequiredService<IContacts>();
        Chats = services.GetRequiredService<IChats>();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        ChatUnreadMessagesFactory = services.GetRequiredService<ChatUnreadMessagesFactory>();
        _pool = new(CreateChatUnreadMessages, DisposeChatUnreadMessages);
    }

    public async Task<Symbol> GetFirstUnreadChat(IReadOnlyCollection<Symbol> chatIds, CancellationToken cancellationToken)
    {
        foreach (var chatId in chatIds) {
            var notificationMode = await AccountSettings.GetChatNotificationMode(chatId, cancellationToken).ConfigureAwait(false);
            if (notificationMode != ChatNotificationMode.Muted)
                continue;

            var unreadCount = await GetCount(chatId, cancellationToken).ConfigureAwait(false);
            if (unreadCount.Value > 0)
                return chatId;
        }

        return Symbol.Empty;
    }

    public async Task<MaybeTrimmed<int>> GetUnreadChatCount(IEnumerable<Symbol> chatIds, CancellationToken cancellationToken)
    {
        var counts = await GetCounts(chatIds, cancellationToken);
        var count = counts.Sum(x => x.Value > 0 ? 1 : 0);
        return (count, MaxUnreadChatCount);
    }

    public async Task<MaybeTrimmed<int>> GetCount(IEnumerable<Symbol> chatIds, CancellationToken cancellationToken)
    {
        var counts = await GetCounts(chatIds, cancellationToken);
        return counts.Sum(ChatUnreadMessages.MaxCount);
    }

    public async Task<MaybeTrimmed<int>> GetCount(Symbol chatId, CancellationToken cancellationToken)
    {
        var unreadMessages = await _pool.Rent(chatId, cancellationToken).ConfigureAwait(false);
        return await unreadMessages.Resource.GetCount(cancellationToken);
    }

    public async Task<bool> HasMentions(Symbol chatId, CancellationToken cancellationToken)
    {
        var unreadMessages = await _pool.Rent(chatId, cancellationToken).ConfigureAwait(false);
        return await unreadMessages.Resource.HasMentions(cancellationToken);
    }

    private Task<List<MaybeTrimmed<int>>> GetCounts(IEnumerable<Symbol> chatIds, CancellationToken cancellationToken)
        => chatIds.Select(x => GetCount(x, cancellationToken)).Collect();

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var cContactIds = await Computed.Capture(() => Contacts.ListIds(Session, cancellationToken)).ConfigureAwait(false);
        var changes = cContactIds.Changes(cancellationToken).ConfigureAwait(false);
        await foreach (var c in changes) {
            var chatIds = c.Value.Select(cid => cid.GetChatId()).ToList();
            var removedChatIds = _leases.Keys.Except(chatIds);
            var addedChatIds = chatIds.Except(_leases.Keys);

            foreach (var chatId in removedChatIds) {
                _leases.Remove(chatId, out var removed);
                removed?.Dispose();
            }
            var newLeases = await addedChatIds
                .Select(id => _pool.Rent(id, cancellationToken).AsTask())
                .Collect()
                .ConfigureAwait(false);
            foreach (var lease in newLeases)
                _leases.Add(lease.Key, lease);
        }
    }

    private Task<ChatUnreadMessages> CreateChatUnreadMessages(Symbol chatId, CancellationToken cancellationToken)
        => Task.FromResult(ChatUnreadMessagesFactory.Get(chatId));

    private ValueTask DisposeChatUnreadMessages(Symbol chatId, ChatUnreadMessages chatUnreadMessages)
    {
        chatUnreadMessages.Dispose();
        return ValueTask.CompletedTask;
    }
}
