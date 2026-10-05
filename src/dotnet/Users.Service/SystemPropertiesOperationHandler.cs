using ActualChat.Operations;
using ActualLab.CommandR.Operations;
using ActualLab.Fusion.Internal;

namespace ActualChat.Users;

/// <summary>
/// Applies the two <see cref="ISystemProperties"/> maintenance commands on every host, which is
/// what their <c>Everywhere</c> flag means. Neither effect is an invalidation call, so nothing
/// carries it but the operation's completion - see <see cref="IEveryHostOperationHandler"/>.
/// </summary>
public sealed class SystemPropertiesOperationHandler(IServiceProvider services) : IEveryHostOperationHandler
{
    public Task OnOperationCompleted(Operation operation, bool isOrigin, CancellationToken cancellationToken)
    {
        switch (operation.Command) {
        case SystemPropertiesBackend_InvalidateEverything backendCommand:
            if (backendCommand.Everywhere || isOrigin)
                ComputedRegistry.InvalidateEverything();
            break;
        case SystemPropertiesBackend_PruneComputedGraph backendCommand:
            if (backendCommand.Everywhere || isOrigin)
                _ = services.GetRequiredService<ComputedGraphPruner>().PruneOnce(CancellationToken.None);
            break;
        case SystemProperties_InvalidateEverything command:
            if (command.Everywhere || isOrigin)
                ComputedRegistry.InvalidateEverything();
            break;
        case SystemProperties_PruneComputedGraph command:
            if (command.Everywhere || isOrigin)
                _ = services.GetRequiredService<ComputedGraphPruner>().PruneOnce(CancellationToken.None);
            break;
        }
        return Task.CompletedTask;
    }
}
