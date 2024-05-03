using ActualChat.Search.Module;
using ActualLab.Interception;

namespace ActualChat.Search;

public abstract class ContactIndexer(IServiceProvider services)
    : ShardWorker(services, ShardScheme.ContactIndexerBackend), INotifyInitialized
{
    protected const int SyncBatchSize = 1000;
    private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromMinutes(5);
    private readonly TaskCompletionSource _whenInitialized = new ();
    private SearchSettings? _settings;
    private IContactIndexStatesBackend? _indexedChatsBackend;
    private OpenSearchConfigurator? _openSearchConfigurator;
    private ICommander? _commander;
    private ILogger? _log;
    private Tracer? _tracer;


    protected ContactIndexingSignal NeedsSync { get; } = new (services);
    protected long MaxVersion => (Clocks.CoarseSystemClock.Now - Settings.ContactIndexingDelay).EpochOffset.Ticks;

    protected SearchSettings Settings => _settings ??= Services.GetRequiredService<SearchSettings>();
    protected OpenSearchConfigurator OpenSearchConfigurator
        => _openSearchConfigurator ??= Services.GetRequiredService<OpenSearchConfigurator>();
    protected IContactIndexStatesBackend ContactIndexStatesBackend
        => _indexedChatsBackend ??= Services.GetRequiredService<IContactIndexStatesBackend>();
    protected ICommander Commander => _commander ??= Services.Commander();
    protected MomentClockSet Clocks { get; } = services.Clocks();
    protected ILogger Log => _log ??= services.LogFor(GetType());
    protected Tracer Tracer => _tracer ??= services.Tracer(GetType());

    public Task WhenInitialized => _whenInitialized.Task;

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnSyncNeeded()
    {
        Log.LogDebug("OnSyncNeeded");
        NeedsSync.SetDelayed();
    }

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled)
            return;

        if (!OpenSearchConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await OpenSearchConfigurator.WhenCompleted.ConfigureAwait(false);

        _whenInitialized.TrySetResult();
        while (!cancellationToken.IsCancellationRequested)
            try {
                Log.LogDebug("Syncing contacts");
                NeedsSync.Reset();
                await Sync(cancellationToken).ConfigureAwait(false);

                Log.LogDebug("Became idle waiting for any event");
                await NeedsSync.WhenSet(MaxIdleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Intended: some other token is cancelled
            }
    }

    protected abstract Task Sync(CancellationToken cancellationToken);

    protected async Task<ContactIndexState> SaveState(ContactIndexState state, CancellationToken cancellationToken)
    {
        var change = state.IsStored() ? Change.Update(state) : Change.Create(state);
        var cmd = new ContactIndexStatesBackend_Change(state.Id, state.Version, change);
        return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }
}
