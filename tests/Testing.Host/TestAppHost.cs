using ActualChat.App.Server;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            // NOTE(AY): These types were heavily rewritten, so let's try to disable this for now.
            // DisposeDbOperationCompletionNotifiers();
            _ = Services.Queues().Purge();
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
            if (!IsGenericTypeImplementation(listener, typeof(DbOperationCompletionListener<>)))
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
