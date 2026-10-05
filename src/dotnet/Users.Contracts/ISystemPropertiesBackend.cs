using ActualChat.Attributes;
using ActualLab.Rpc;

namespace ActualChat.Users;

[BackendService(nameof(HostRole.Api), ServiceMode.Local)]
public interface ISystemPropertiesBackend : IComputeService, IBackendService
{
    [CommandHandler]
    Task OnInvalidateEverything(
        SystemPropertiesBackend_InvalidateEverything command, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnPruneComputedGraph(
        SystemPropertiesBackend_PruneComputedGraph command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
public sealed partial record SystemPropertiesBackend_InvalidateEverything(
    [property: DataMember, Key(0)] bool Everywhere
) : ICommand<Unit>, IBackendCommand;

[DataContract, MessagePackObject]
public sealed partial record SystemPropertiesBackend_PruneComputedGraph(
    [property: DataMember, Key(0)] bool Everywhere
) : ICommand<Unit>, IBackendCommand;
