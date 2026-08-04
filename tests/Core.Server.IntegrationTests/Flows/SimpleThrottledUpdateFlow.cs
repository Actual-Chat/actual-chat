using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Flow(DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class SimpleThrottledUpdateFlow : ThrottledUpdateFlow
{
    protected override TimeSpan ThrottlePeriod => TimeSpan.FromSeconds(2);

    protected override ValueTask Run(CancellationToken cancellationToken)
    {
        Console.Log($"Run: Target={Target}");
        return default;
    }
}
