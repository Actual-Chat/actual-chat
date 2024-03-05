using ActualChat.App.Server;
using ActualChat.Commands;
using ActualChat.Hosting;
using ActualLab.Fusion.EntityFramework.Operations;
using ActualLab.Testing.Output;

namespace ActualChat.Testing.Host;

public class TestAppHost(TestAppHostOptions options, TestOutputHelperAccessor outputAccessor) : AppHost
{
    public TestAppHostOptions Options { get; } = options;
    public TestOutputHelperAccessor OutputAccessor { get; } = outputAccessor;

    public ITestOutputHelper? Output {
        get => OutputAccessor.Output;
        set => OutputAccessor.Output = value;
    }

    public Task WaitForProcessingOfAlreadyQueuedCommands()
        => WaitForProcessingOfAlreadyQueuedCommands(TimeSpan.FromSeconds(2));

    public async Task WaitForProcessingOfAlreadyQueuedCommands(TimeSpan timeout)
    {
        var hostRoles = Services.GetRequiredService<HostInfo>().Roles;
        var backendHostRoles = hostRoles.Where(hr => hr.IsBackend || hr.IsQueue);
        var schedulers = new List<ICommandQueueScheduler>();
        foreach (var hostRole in backendHostRoles) {
            var serviceKey = hostRole.Id.Value;
            var commandQueueSchedulers = Services.GetKeyedServices<ICommandQueueScheduler>(serviceKey);
            schedulers.AddRange(commandQueueSchedulers);
        }

        await Task.WhenAll(schedulers.Select(s => s.ProcessAlreadyQueued(timeout, CancellationToken.None)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            DisposeDbOperationCompletionNotifiers();
            var queues = Services.GetRequiredService<ICommandQueues>();
            _ = queues.Purge(CancellationToken.None);
        }
        base.Dispose(disposing);
    }

    private void DisposeDbOperationCompletionNotifiers()
    {
        // During usual AppHost disposing it dispose inner Host which in turn disposed owned services collection.
        // Microsoft.Extensions.DependencyInjection service provider disposes services sequentially even if them
        // implements IAsyncDisposable.
        // See https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ServiceProviderEngineScope.cs#L156
        // DbOperationCompletionNotifierBase disposing takes at least MaxCommitDuration specified in its Options.
        // See https://github.com/servicetitan/ActualLab.Fusion/blob/master/src/ActualLab.Fusion.EntityFramework/Operations/DbOperationCompletionNotifierBase.cs#L54
        // In our case we MaxCommitDuration is 1 seconds and we have 7 instances
        // of RedisOperationLogChangeNotifier<TDbContext> for each DbContext respectively.
        // Hence AppHost disposing takes at least 7 seconds.
        // To work around this I dispose all instances of DbOperationCompletionNotifiers at once without awaiting
        // their completion during TestAppHost disposing.
        // Apparently it would be better if DbOperationCompletionNotifierBase can bind to host lifetime and
        // stop notifications on host stopping. Then it would be easier to dispose app host faster by stopping it first.

        IEnumerable<IOperationCompletionListener> completionListeners;
        try {
            completionListeners = Services.GetRequiredService<IEnumerable<IOperationCompletionListener>>();
        }
        catch (ObjectDisposedException) {
            // Container has been disposed already. Do nothing.
            return;
        }

        foreach (var listener in completionListeners) {
            if (!IsGenericTypeImplementation(listener, typeof(DbOperationCompletionNotifierBase<,>)))
                continue;
            if (listener is IAsyncDisposable asyncDisposable)
                _ = asyncDisposable.DisposeAsync();
            else if (listener is IDisposable disposable)
                disposable.Dispose();
        }

        bool IsGenericTypeImplementation(object? inst, Type genericTypeDef) {
            if (inst == null)
                return false;
            var type = inst.GetType();
            while (type != null) {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == genericTypeDef)
                    return true;
                type = type.BaseType;
            }
            return false;
        }
    }
}
