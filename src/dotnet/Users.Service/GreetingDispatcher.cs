using ActualChat.Contacts;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Users;

public class GreetingDispatcher(IServiceProvider services) : WorkerBase, IHasServices
{
    private static readonly TimeSpan MaxIdleInterval = TimeSpan.FromMinutes(5);
    private readonly IMutableState<bool> _needsGreeting = services.StateFactory().NewMutable<bool>();
    private const int SelectBatchSize = 100;
    private DbHub<UsersDbContext>? _dbHub;
    private ICommander? _commander;
    private ILogger? _log;

    public IServiceProvider Services { get; } = services;
    private DbHub<UsersDbContext> DbHub => _dbHub ??= Services.DbHub<UsersDbContext>();
    private ICommander Commander => _commander ??= Services.Commander();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public void OnGreetingNeeded()
        => _needsGreeting.Value = true;

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
                await _needsGreeting.When(needsGreeting => needsGreeting, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        { }
    }

    private async Task<bool> DispatchBatch(CancellationToken cancellationToken)
    {
        var dbContext = DbHub.CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        var dbAccounts = await dbContext.Accounts.Where(x => !x.IsGreetingCompleted)
            .Take(SelectBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbAccounts.Count == 0) {
            _needsGreeting.Value = false;
            return false;
        }

        _needsGreeting.Value = false;
        foreach (var userId in dbAccounts.Select(dbAccount => new UserId(dbAccount.Id)))
            try {
                await Commander.Call(new ContactsBackend_Greet(userId), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to greet account #{UserId}", userId);
            }
        return true;
    }
}
