using ActualChat.Hosting;

namespace ActualChat.App.Server.Initializers;

public class ExecuteDbInitializers(IServiceProvider services): IAggregateInitializer
{
    public async Task InvokeAll(CancellationToken cancellationToken)
    {
        var hostInfo = services.HostInfo();
        if (hostInfo.HasRole(HostRole.OneApiServer) && !hostInfo.HasRole(HostRole.OneServer))
            return; // This role doesn't (and can't) run DB initializers

        // InitializeSchema
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeSchema),
            (x, ct) => x.InitializeSchema(ct),
            cancellationToken
        ).ConfigureAwait(false);

        var options = services.GetService<DbInitializeOptions>() ?? DbInitializeOptions.Default;
        if (!options.InitializeData)
            return;

        // InitializeData
        await InvokeDbInitializers(
            nameof(IDbInitializer.InitializeData),
            (x, ct) => x.InitializeData(ct),
            cancellationToken
        ).ConfigureAwait(false);

        // RepairData
        await InvokeDbInitializers(
            nameof(IDbInitializer.RepairData),
            x => x.ShouldRepairData,
            (x, ct) => x.RepairData(ct),
            cancellationToken
        ).ConfigureAwait(false);

        // VerifyData
        await InvokeDbInitializers(
            nameof(IDbInitializer.VerifyData),
            x => x.ShouldVerifyData,
            (x, ct) => x.VerifyData(ct),
            cancellationToken
        ).ConfigureAwait(false);
    }

    private Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
        => InvokeDbInitializers(name, _ => true, invoker, cancellationToken);

    private async Task InvokeDbInitializers(
        string name,
        Func<IDbInitializer, bool> mustInvokePredicate,
        Func<IDbInitializer, CancellationToken, Task> invoker,
        CancellationToken cancellationToken)
    {
        var log = services.LogFor(GetType());
        var runningTaskSources = services.GetServices<IDbInitializer>()
            .ToDictionary(x => x, _ => TaskCompletionSourceExt.New<bool>());
        var runningTasks = runningTaskSources
            .ToDictionary(kv => kv.Key, kv => (Task)kv.Value.Task);
        foreach (var (dbInitializer, _) in runningTasks)
            dbInitializer.RunningTasks = runningTasks;
        var tasks = runningTaskSources
            .Select(kv => mustInvokePredicate.Invoke(kv.Key) ? InvokeOne(kv.Key, kv.Value) : Task.CompletedTask)
            .ToArray();

        try {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally {
            foreach (var (dbInitializer, _) in runningTasks)
                dbInitializer.RunningTasks = null!;
        }
        return;

        async Task InvokeOne(IDbInitializer dbInitializer, TaskCompletionSource<bool> initializedSource)
        {
            using var _ = dbInitializer.Activate();
            var dbInitializerName = $"{dbInitializer.GetType().GetName()}.{name}";
            try {
                log.LogInformation("{DbInitializer} started", dbInitializerName);
                var task = invoker.Invoke(dbInitializer, cancellationToken);
                if (task.IsCompletedSuccessfully)
                    log.LogInformation("{DbInitializer} completed synchronously (skipped?)", dbInitializerName);
                else {
                    await task.ConfigureAwait(false);
                    log.LogInformation("{DbInitializer} completed", dbInitializerName);
                }
                initializedSource.TrySetResult(default);
            }
            catch (OperationCanceledException) {
                initializedSource.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception e) {
                log.LogError(e, "{DbInitializer} failed", dbInitializerName);
                initializedSource.TrySetException(e);
                throw;
            }
        }
    }
}
