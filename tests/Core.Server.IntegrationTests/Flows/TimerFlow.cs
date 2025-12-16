using System.Globalization;
using ActualChat.Flows;
using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Core.Server.IntegrationTests.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial class TimerFlow : Flow<Unit>
{
    private static bool _threwRerouteException;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public int RemainingCount { get; private set; }
    [DataMember(Order = 1), MemoryPackOrder(1)]
    public TimeSpan Period { get; private set; }

    protected override ValueTask Init(CancellationToken cancellationToken)
    {
        Console.Log($"Reset @ {Runtime.Services.MeshWatcher().ThisNode.Ref.Value}");
        var args = Id.SplitArguments("", "1", "1");
        RemainingCount = int.Parse(args[1], CultureInfo.InvariantCulture);
        Period = TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture));
        Console.Log($"Reset: RemainingCount={RemainingCount}, Period={Period.ToShortString()}");
        return default;
    }

    protected override async ValueTask Resume(CancellationToken cancellationToken)
    {
        Console.Log($"Resume @ {Runtime.Services.MeshWatcher().ThisNode.Ref.Value}");
        Runtime.DefaultResumeDelayQuanta = Period / 2;

        if (RemainingCount > 0) {
            if (!_threwRerouteException) {
                _threwRerouteException = true;
                throw RpcRerouteException.MustReroute();
            }

            RemainingCount--;
            Runtime.StageResumeIn(Period);
            Console.Log($"Will resume in {Period.ToShortString()}, RemainingCount={RemainingCount}");
            if (RemainingCount == 1) {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                await Runtime.Commit(cancellationToken).ConfigureAwait(false);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                Console.Log("Post-commit message (should be visible due to AutoCommit)");
            }
        }
        else {
            SetResult(default);
            Console.Log("Post-set-result message");
        }
    }
}
