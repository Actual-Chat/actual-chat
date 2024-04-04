using ActualChat.Hosting;

namespace ActualChat.App.Server.Initializers;

public class ExecuteModuleInitializers(IServiceProvider services): IAggregateInitializer
{
    public async Task InvokeAll(CancellationToken cancellationToken)
    {
        var moduleInitializers = services.GetServices<IModuleInitializer>();
        var initializeTasks = moduleInitializers.Select(instance => instance.Initialize(cancellationToken)).ToArray();
        await Task.WhenAll(initializeTasks).ConfigureAwait(false);
    }
}
