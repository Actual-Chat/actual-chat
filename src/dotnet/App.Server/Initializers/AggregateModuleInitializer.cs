namespace ActualChat.App.Server.Initializers;

/// <summary>
/// Orchestrates all <see cref="IModuleInitializer"/> instances to run module-specific startup logic.
/// </summary>
public class AggregateModuleInitializer(IServiceProvider services): WorkerBase
{
    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var moduleInitializers = services.GetServices<IModuleInitializer>();
        var initializeTasks = moduleInitializers.Select(instance => instance.Initialize(cancellationToken)).ToArray();
        await Task.WhenAll(initializeTasks).ConfigureAwait(false);
    }
}
