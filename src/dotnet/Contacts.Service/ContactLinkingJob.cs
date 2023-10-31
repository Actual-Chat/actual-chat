using ActualChat.Contacts.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Interception;

namespace ActualChat.Contacts;

internal class ContactLinkingJob : WorkerBase, IComputeService, IHasServices, INotifyInitialized
{
    private const int SelectBatchSize = 100;
    private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromMinutes(5);
    private readonly IMutableState<bool> _needsSync;
    private DbHub<ContactsDbContext>? _dbHub;
    private ICommander? _commander;
    private ILogger? _log;
    private IAccountsBackend? _accountsBackend;

    private IAccountsBackend AccountsBackend => _accountsBackend ??= Services.GetRequiredService<IAccountsBackend>();
    public IServiceProvider Services { get; }

    private DbHub<ContactsDbContext> DbHub => _dbHub ??= Services.DbHub<ContactsDbContext>();
    private ICommander Commander => _commander ??= Services.Commander();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public ContactLinkingJob(IServiceProvider services)
    {
        Services = services;
        _needsSync = services.StateFactory().NewMutable<bool>();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    public void OnSyncNeeded()
        => _needsSync.Value = true;

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(1, MaxIdleInterval.TotalSeconds);
        return AsyncChainExt.From(DispatchAll)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .CycleForever()
            .Run(cancellationToken);
    }

    private async Task DispatchAll(CancellationToken cancellationToken)
    {
        try
        {
            if (!await DispatchBatch(cancellationToken).ConfigureAwait(false)) {
                var cts = cancellationToken.CreateLinkedTokenSource();
                cts.CancelAfter(MaxIdleInterval);
                await _needsSync.When(needs => needs, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        { }
    }

    private async Task<bool> DispatchBatch(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Default.Region();
        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);
        var dbExternalContactLinks = await dbContext.ExternalContactLinks.ForUpdate()
            .Where(x => !x.IsChecked)
            .Take(SelectBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbExternalContactLinks.Count == 0)
            return false;

        _needsSync.Value = false;
        using var _2 = Tracer.Default.Region($"Check {dbExternalContactLinks.Count} external contact links");
        foreach (var link in dbExternalContactLinks)
            try
            {
                var userId = await FindUserId(link, cancellationToken).ConfigureAwait(false);
                var ownerId = new ExternalContactId(link.DbExternalContactId).OwnerId;
                await CreateContact(ownerId, userId, cancellationToken).ConfigureAwait(false);
                link.IsChecked = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to link external contact #{ExternalContactId} by '{ExternalContactLink}'", link.DbExternalContactId, link.Value);
            }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<UserId> FindUserId(DbExternalContactLink link, CancellationToken cancellationToken)
    {
        var phoneHash = link.ToPhoneHash();
        if (!phoneHash.IsNullOrEmpty())
            return AccountsBackend.GetIdByPhoneHash(phoneHash, cancellationToken);

        var emailHash = link.ToEmailHash();
        if (!emailHash.IsNullOrEmpty())
            return AccountsBackend.GetIdByEmailHash(emailHash, cancellationToken);

        Log.LogError("Could not recognize external contact link type for {ExternalContactLink}", link.Value);
        return Task.FromResult(UserId.None);
    }

    private async Task CreateContact(UserId ownerId, UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone || ownerId == userId)
            return;

        var peerChatId = new PeerChatId(ownerId, userId);
        var contactId = new ContactId(ownerId, peerChatId);

        try {
            var contact = new Contact(contactId);
            var cmd = new ContactsBackend_Change(contactId, null, Change.Create(contact));
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to create contact #{ContactId} from external contact", contactId);
        }
    }
}
