using ActualChat.Contacts;
using ActualLab.Interception;

namespace ActualChat.UI.App.Services;

public abstract class IncomingShareSuggestions(IServiceProvider services) : WorkerBase, INotifyInitialized
{
    private static readonly TimeSpan ContactSuggestionThrottlingInterval = TimeSpan.FromMinutes(1);
    private readonly Channel<ContactId> _channel = Channel.CreateBounded<ContactId>(
        new BoundedChannelOptions(100) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Dictionary<ContactId, Moment> _lastSuggestedAt = new();

    protected IServiceProvider Services { get; } = services;
    protected IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    protected Session Session => field ??= Services.GetRequiredService<Session>();
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();
    private MomentClockSet Clocks => field ??= Services.Clocks();

    void INotifyInitialized.Initialized()
        => this.Start();

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
        => _channel.Writer.TryWrite(contactId);

    protected override Task OnRun(CancellationToken cancellationToken)
        => AsyncChain.From(ProcessQueue)
            .LogError(Log)
            .RetryForever(RetryDelaySeq.Exp(0.1, 5), Log)
            .RunIsolated(cancellationToken);

    private async Task ProcessQueue(CancellationToken cancellationToken)
    {
        await foreach (var contactId in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            try {
                var now = Clocks.SystemClock.Now;
                if (_lastSuggestedAt.TryGetValue(contactId, out var lastAt)
                    && now - lastAt < ContactSuggestionThrottlingInterval)
                    continue;

                _lastSuggestedAt[contactId] = now;
                await SuggestInternal(contactId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to suggest incoming share to contact #{ContactId}", contactId);
            }
    }

    protected virtual Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
