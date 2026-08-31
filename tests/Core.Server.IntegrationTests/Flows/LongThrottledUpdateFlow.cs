using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

/// <summary>
/// A <see cref="ThrottledUpdateFlow"/> whose throttle period is long enough to never
/// elapse mid-test - so tests asserting "the run didn't happen" can't race it.
/// </summary>
[Flow(DelayQuanta = 0)]
[DataContract, MessagePackObject(true)]
public sealed partial class LongThrottledUpdateFlow : ThrottledUpdateFlow
{
    protected override TimeSpan ThrottlePeriod => TimeSpan.FromMinutes(1);
    protected override ValueTask Run(CancellationToken cancellationToken)
    {
        Console.Log($"Run: Target={Target}");
        return default;
    }
}
