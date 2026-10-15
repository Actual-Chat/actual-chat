namespace ActualChat.App.Server.Initializers;

/// <summary>
/// Orchestrates all <see cref="IModuleInitializer"/> instances to run module-specific startup logic.
/// </summary>
public sealed class AggregateModuleInitializer(IServiceProvider services)
{
    public async Task Run(CancellationToken cancellationToken)
    {
        var moduleInitializers = services.GetServices<IModuleInitializer>();
        var initializeTasks = moduleInitializers.Select(instance => instance.Initialize(cancellationToken)).ToArray();
        await Task.WhenAll(initializeTasks).ConfigureAwait(false);
    }
}
