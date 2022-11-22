using ActualChat.Contacts;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ContactUI
{
    private readonly AsyncLock _asyncLock = new (ReentryMode.CheckedPass);

    private IServiceProvider Services { get; }
    private IStateFactory StateFactory { get; }
    private Session Session { get; }
    private IChats Chats { get; }
    private IContacts Contacts { get; }
    private UnreadMessages UnreadMessages { get; }
    private MomentClockSet Clocks { get; }

    private Moment Now => Clocks.SystemClock.Now;

    public IStoredState<ContactId> SelectedContactId { get; }
    public ISyncedState<ImmutableHashSet<PinnedContact>> PinnedContacts { get; }

    public ContactUI(IServiceProvider services)
    {
        Services = services;
        StateFactory = services.StateFactory();
        Session = services.GetRequiredService<Session>();
        Chats = services.GetRequiredService<IChats>();
        Contacts = services.GetRequiredService<IContacts>();
        UnreadMessages = services.GetRequiredService<UnreadMessages>();
        Clocks = services.Clocks();

        var localSettings = services.LocalSettings();
        var accountSettings = services.AccountSettings().WithPrefix(nameof(ChatUI));
        SelectedContactId = StateFactory.NewKvasStored<ContactId>(new(localSettings, nameof(SelectedContactId)));
        PinnedContacts = StateFactory.NewKvasSynced<ImmutableHashSet<PinnedContact>>(
            new(accountSettings, nameof(PinnedContacts)) {
                InitialValue = ImmutableHashSet<PinnedContact>.Empty,
                Corrector = FixPinnedContacts,
            });
    }

    [ComputeMethod]
    public virtual Task<bool> IsSelected(ContactId contactId)
        => Task.FromResult(SelectedContactId.Value.Id == contactId);

    [ComputeMethod]
    public virtual Task<bool> IsPinned(ContactId contactId)
        => Task.FromResult(PinnedContacts.Value.Contains(contactId));

    public ValueTask Pin(ContactId contactId) => SetPinState(contactId, true);
    public ValueTask Unpin(ContactId contactId) => SetPinState(contactId, false);

    public ValueTask SetPinState(ContactId contactId, bool mustPin)
    {
        if (contactId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(contactId));

        return UpdatePinnedContacts(
            pinnedChats => mustPin
                ? pinnedChats.Add(new PinnedContact(contactId, Now))
                : pinnedChats.Remove(contactId)
            );
    }

    // Protected & private methods

    [ComputeMethod]
    protected virtual async Task<ImmutableList<ContactSummary>> ListSummaries(CancellationToken cancellationToken)
    {
        var contacts = await Contacts.ListContacts(Session, cancellationToken).ConfigureAwait(false);
        var summaries = await contacts
            .Select(async c => {
                var hasMentions = await UnreadMessages.HasMentions(c.ChatId, cancellationToken).ConfigureAwait(false);
                var unreadMessageCount = await UnreadMessages.GetCount(c.ChatId, cancellationToken).ConfigureAwait(false);
                return new ContactSummary(c, hasMentions, unreadMessageCount);
            })
            .Collect()
            .ConfigureAwait(false);

        var result = summaries
            .OrderByDescending(c => c.HasMentions).ThenByDescending(c => c.Contact.TouchedAt)
            .Select(c => c.Contact)
            .ToImmutableList();
        return result;
    }

    private async ValueTask UpdatePinnedContacts(
        Func<ImmutableHashSet<PinnedContact>, ImmutableHashSet<PinnedContact>> updater,
        CancellationToken cancellationToken = default)
    {
        using var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false);
        var originalValue = PinnedContacts.Value;
        var updatedValue = updater.Invoke(originalValue);
        if (ReferenceEquals(originalValue, updatedValue))
            return;

        updatedValue = await FixPinnedContacts(updatedValue, cancellationToken).ConfigureAwait(false);
        PinnedContacts.Value = updatedValue;
    }

    private async ValueTask<ImmutableHashSet<PinnedContact>> FixPinnedContacts(
        ImmutableHashSet<PinnedContact> pinnedContacts,
        CancellationToken cancellationToken = default)
    {
        if (pinnedContacts.Count < 32)
            return pinnedContacts;

        var oldBoundary = Now - TimeSpan.FromDays(365);
        var contactRules = await pinnedContacts
            .Where(c => c.Recency < oldBoundary)
            .Select(async c => {
                var rules = await Chats.GetRules(Session, c.Id.ChatId, default).ConfigureAwait(false);
                return (Contact: c, Rules: rules);
            })
            .Collect()
            .ConfigureAwait(false);

        var result = pinnedContacts;
        foreach (var (c, rules) in contactRules) {
            if (rules.CanRead())
                continue;
            result = result.Remove(c);
        }
        return result;
    }
}
