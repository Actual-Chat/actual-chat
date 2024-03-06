using ActualChat.Hosting;

namespace ActualChat.App.Server.Initializers;

public class ExecuteModuleInitializers(IServiceProvider services): IAggregateInitializer
{
    public async Task InvokeAll(CancellationToken cancellationToken)
        => await Task.WhenAll(services.GetServices<IModuleInitializer>()
                .Select(instance => instance.Initialize(cancellationToken))
            ).ConfigureAwait(false);
}
