using System.Collections.Concurrent;
using ActualChat.Flows;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[Flow(DelayQuanta = 0)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// Inherited Key-bearing members from ThrottledFlow are reported as "inconsistent" by Nerdbank's
// analyzer here even though every visible derived member is either keyed or PropertyShape-Ignored
// — analyzer's inheritance scan trips on a path that even [ConstructorShape] doesn't address.
// Suppress just for this test flow; runtime serialization works fine.
#pragma warning disable NBMsgPack001
public sealed partial class FailingThrottledUpdateFlow : ThrottledUpdateFlow
#pragma warning restore NBMsgPack001
{
    /// <summary>
    /// Per-target counters tracking how many times Run() has been called.
    /// Tests set <see cref="FailUntilCallCount"/> and the flow throws until
    /// the call count for its target reaches that threshold.
    /// </summary>
    public static readonly ConcurrentDictionary<string, int> CallCounts = new();

    /// <summary>
    /// When > 0, Run() throws for the first N calls (per target).
    /// Set to <see cref="int.MaxValue"/> to make it always fail.
    /// </summary>
    [PolyType.PropertyShape(Ignore = true)]
    public static int FailUntilCallCount { get; set; }

    [PolyType.PropertyShape(Ignore = true)]
    protected override TimeSpan ThrottlePeriod => TimeSpan.FromSeconds(2);
    [PolyType.PropertyShape(Ignore = true)]
    protected override int MaxFailCount => 3;
    [PolyType.PropertyShape(Ignore = true)]
    protected override TimeSpan RetryDelay => TimeSpan.FromMilliseconds(200);

    protected override ValueTask Run(CancellationToken cancellationToken)
    {
        var count = CallCounts.AddOrUpdate(Target, 1, (_, c) => c + 1);
        Console.Log($"Run: Target={Target}, CallCount={count}");
        if (count <= FailUntilCallCount)
            throw new InvalidOperationException($"Simulated failure #{count}");
        return default;
    }
}
