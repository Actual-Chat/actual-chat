using ActualChat.Hosting;

namespace ActualChat.Users;

public class SystemProperties : ISystemProperties
{
    private const string MinMauiClientVersion = "0.121";
    private const string MinWasmClientVersion = "0.121";
    private static readonly Task<string?> NullStringTask = Task.FromResult((string?)null);

    private static readonly Dictionary<AppKind, string> MinClientVersions = new() {
        { AppKind.MauiApp, MinMauiClientVersion },
        { AppKind.WasmApp, MinWasmClientVersion },
    };
    private static readonly Dictionary<AppKind, Task<string?>> MinClientVersionTasks =
        MinClientVersions.ToDictionary(kv => kv.Key, kv => Task.FromResult(kv.Value))!;

    private MomentClockSet Clocks { get; }

    public SystemProperties(MomentClockSet clocks)
        => Clocks = clocks;

    // Not a [ComputeMethod]!
    public Task<double> GetTime(CancellationToken cancellationToken)
        => Task.FromResult(Clocks.SystemClock.Now.EpochOffset.TotalSeconds);

    // Not a [ComputeMethod]!
    public Task<string?> GetMinClientVersion(AppKind appKind, CancellationToken cancellationToken)
        => MinClientVersionTasks.GetValueOrDefault(appKind)
            ?? NullStringTask;
}
