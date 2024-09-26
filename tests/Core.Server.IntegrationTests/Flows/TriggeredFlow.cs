using System.Globalization;
using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TriggeredFlow : Flow
{
    
    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        return FlowTransition.None;
    }

    protected async Task<FlowTransition> OnTimer(CancellationToken cancellationToken)
    {
        return FlowTransition.None;
    }


}
