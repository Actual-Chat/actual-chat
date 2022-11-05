using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RecentChatsUI : WorkerBase
{
    private volatile ImmutableList<Chat> _listIncludingSelectedCached = ImmutableList<Chat>.Empty;

    private Session Session { get; }
    private IContacts Contacts { get; }
    private IChats Chats { get; }
    private UnreadMessages UnreadMessages { get; }
    private ChatUI ChatUI { get; }

    public RecentChatsUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Contacts = services.GetRequiredService<IContacts>();
        Chats = services.GetRequiredService<IChats>();
        UnreadMessages = services.GetRequiredService<UnreadMessages>();
        ChatUI = services.GetRequiredService<ChatUI>();
        Start();
    }

    [ComputeMethod]
    public virtual Task<ImmutableList<Chat>> ListIncludingSelected()
        => Task.FromResult(_listIncludingSelectedCached);

    [ComputeMethod]
    public virtual async Task<Chat?> GetSelectedChat(CancellationToken cancellationToken)
    {
        var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        if (!ParsedChatId.TryParse(selectedChatId, out _))
            selectedChatId = Symbol.Empty;
        if (selectedChatId.IsEmpty)
            return null;

        return await Chats.Get(Session, selectedChatId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Chat?> GetSelectedOrDefaultChat(CancellationToken cancellationToken)
    {
        var selectedChat = await GetSelectedChat(cancellationToken).ConfigureAwait(false);
        if (selectedChat != null)
            return selectedChat;

        var chats = await ListIncludingSelected().ConfigureAwait(false);
        return chats.FirstOrDefault(x => x.Id == Constants.Chat.DefaultChatId) ?? chats.FirstOrDefault();
    }

    // Protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => InvalidateListIncludingSelected(cancellationToken);

    [ComputeMethod]
    protected virtual async Task<ImmutableList<Chat>> ListIncludingSelectedInternal(CancellationToken cancellationToken)
    {
        var selectedChat = await GetSelectedChat(cancellationToken).ConfigureAwait(false);
        var chats = await List(cancellationToken).ConfigureAwait(false);
        if (selectedChat != null && !chats.Contains(selectedChat))
            chats = chats.Insert(0, selectedChat);

        return chats;
    }

    [ComputeMethod]
    protected virtual async Task<ImmutableList<Chat>> List(CancellationToken cancellationToken)
    {
        var contacts = await Contacts.ListContacts(Session, cancellationToken).ConfigureAwait(false);
        var contactsWithMentions = (await contacts
            .Select(async c => {
                if (c.Chat == null)
                    return (Contact: c, HasMentions: false);
                var hasMentions = await UnreadMessages.HasMentions(c.ChatId, cancellationToken).ConfigureAwait(false);
                return (Contact: c, HasMentions: hasMentions);
            })
            .Collect()
            .ConfigureAwait(false)
            ).ToList();

        var result = contactsWithMentions
            .OrderByDescending(c => c.HasMentions).ThenByDescending(c => c.Contact.TouchedAt)
            .Select(c => c.Contact.Chat)
            .SkipNullItems()
            .ToImmutableList();
        return result;
    }

    private async Task InvalidateListIncludingSelected(CancellationToken cancellationToken)
    {
        var cListIncludingSelected = await Computed
            .Capture(() => ListIncludingSelectedInternal(cancellationToken))
            .ConfigureAwait(false);
        var changes = cListIncludingSelected.Changes(cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var listIncludingSelected = c.Value;

            if (!listIncludingSelected.SequenceEqual(_listIncludingSelectedCached)) {
                Interlocked.Exchange(ref _listIncludingSelectedCached, listIncludingSelected);
                using (Computed.Invalidate())
                    _ = ListIncludingSelected();
            }
        }
    }
}
