using ActualChat.Contacts;

namespace ActualChat.UI.App.Services;

public class IncomingShareSuggestions(IServiceProvider services) : ProcessorBase
{
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<ContactId, Debouncer<ContactId>> _debouncers = new();

    protected IServiceProvider Services { get; } = services;
    protected IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    protected Session Session => field ??= Services.GetRequiredService<Session>();
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();

    public async Task Push(ChatId chatId)
    {
        try {
            var ownAccount = await Accounts.GetOwn(Session, StopToken).ConfigureAwait(false);
            if (ownAccount.IsGuest)
                return;

            var contactId = ContactId.NewAny(ownAccount.Id, chatId);
            Push(contactId);
        }
        catch (Exception e) {
            if (!e.IsCancellationOf(StopToken))
                Log.LogError(e, "Failed to suggest incoming share to chat #{ChatId}", chatId);
        }
    }

    public void Push(ContactId contactId)
    {
        var debouncer = _debouncers.GetOrAdd(contactId, CreateDebouncer);
        debouncer.Throttle(contactId);
    }

    private Debouncer<ContactId> CreateDebouncer(ContactId _)
        => Debouncer.New<ContactId, IncomingShareSuggestions>(ThrottleInterval, this,
            static (id, self) => self.SuggestInternal(id, self.StopToken));

    protected virtual Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
