using ActualChat.Operations;
using ActualLab.CommandR.Operations;

namespace ActualChat.Chat;

/// <summary>
/// Invalidates everything on every host after the default chat is created. That command writes a
/// lot of state directly rather than through the compute methods that read it, so there is no set
/// of invalidation calls to record - see <see cref="IEveryHostOperationHandler"/>.
/// </summary>
public sealed class ChatsUpgradeOperationHandler : IEveryHostOperationHandler
{
    public Task OnOperationCompleted(Operation operation, bool isOrigin, CancellationToken cancellationToken)
    {
        if (operation.Command is ChatsUpgradeBackend_CreateDefaultChat)
            ComputedRegistry.InvalidateEverything();
        return Task.CompletedTask;
    }
}
