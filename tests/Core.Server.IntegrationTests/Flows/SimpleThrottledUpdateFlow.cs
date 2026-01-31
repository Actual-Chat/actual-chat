using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Flow(DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class SimpleThrottledUpdateFlow : ThrottledUpdateFlow
{
    protected override TimeSpan ThrottlePeriod => TimeSpan.FromSeconds(2);

    protected override ValueTask Run(CancellationToken cancellationToken)
    {
        Console.Log($"Run: Target={Target}");
        return default;
    }
}
