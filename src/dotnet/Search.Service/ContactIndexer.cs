using ActualChat.Search.Module;
using ActualLab.Interception;

namespace ActualChat.Search;

public abstract class ContactIndexer(IServiceProvider services)
    : ShardWorker(services, ShardScheme.ContactIndexingWorker), INotifyInitialized
{
    protected const int SyncBatchSize = 1000;
    private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromMinutes(5);
    private SearchSettings? _settings;
    private IContactIndexStatesBackend? _indexedChatsBackend;
    private ElasticConfigurator? _elasticConfigurator;
    private ICommander? _commander;

    protected ContactIndexingSignal NeedsSync { get; } = new (services);
    private MomentClockSet Clocks { get; } = services.Clocks();
    // we assume that ClockBasedVersionGenerator.DefaultCoarse is used
    protected long MaxVersion => (Clocks.CoarseSystemClock.Now - Settings.ContactIndexingDelay).EpochOffset.Ticks;
    protected IContactIndexStatesBackend ContactIndexStatesBackend => _indexedChatsBackend ??= Services.GetRequiredService<IContactIndexStatesBackend>();
    protected ICommander Commander => _commander ??= Services.Commander();
    private ElasticConfigurator ElasticConfigurator => _elasticConfigurator ??= Services.GetRequiredService<ElasticConfigurator>();
    private SearchSettings Settings => _settings ??= Services.GetRequiredService<SearchSettings>();

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnSyncNeeded()
        => NeedsSync.SetDelayed();

    protected override async Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        if (!Settings.IsSearchEnabled)
            return;

        if (!ElasticConfigurator.WhenCompleted.IsCompletedSuccessfully)
            await ElasticConfigurator.WhenCompleted.ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
            try {
                NeedsSync.Reset();
                await Sync(cancellationToken).ConfigureAwait(false);

                await NeedsSync.WhenSet(MaxIdleInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    protected abstract Task Sync(CancellationToken cancellationToken);

    protected async Task<ContactIndexState> SaveState(ContactIndexState state, CancellationToken cancellationToken)
    {
        var change = state.IsStored() ? Change.Update(state) : Change.Create(state);
        var cmd = new ContactIndexStatesBackend_Change(state.Id, state.Version, change);
        return await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
    }
}
