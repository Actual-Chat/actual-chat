using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace ActualChat.Benchmarks;

public static class BenchmarkJobs
{
    // In-process isn't a preference: BenchmarkDotNet 0.15.x doesn't know the .NET 11 runtime
    // moniker, so its default csproj toolchain throws in SDK validation before a single
    // benchmark runs. Switch back to the default toolchain once BDN recognizes the TFM.
    public static readonly Job ShortRunInProcess =
        Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance);
}

/// <summary>
/// Default config for this assembly's benchmarks - see <see cref="BenchmarkJobs"/>.
/// </summary>
public sealed class InProcessShortRunConfig : ManualConfig
{
    public InProcessShortRunConfig()
        => AddJob(BenchmarkJobs.ShortRunInProcess);
}
