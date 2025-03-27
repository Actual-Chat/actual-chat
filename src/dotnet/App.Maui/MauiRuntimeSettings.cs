namespace ActualChat.App.Maui;

public static class MauiRuntimeSettings
{
    private static readonly Tracer Tracer = MauiDiagnostics.Tracer[nameof(MauiRuntimeSettings)];

    public static void Apply()
    {
        var cpuCount = HardwareInfo.ProcessorCount;
        var gcParams = Environment.GetEnvironmentVariable("MONO_GC_PARAMS") ?? "";
        Tracer.Point($"CPU count: {cpuCount}, MONO_GC_PARAMS: {gcParams}");

        ThreadPool.GetMinThreads(out var originalMinW, out var originalMinIO);
        ThreadPool.GetMaxThreads(out var maxW, out var maxIO);

        var minW = Math.Max(originalMinW, cpuCount + 4);
        var minIO = Math.Max(originalMinIO, cpuCount);
        ThreadPool.SetMinThreads(minW, minIO);
        ThreadPool.GetMinThreads(out minW, out minIO);
        Tracer.Point($"ThreadPool settings: ({minW}, {minIO}) .. ({maxW}, {maxIO}), original: ({minW}, {minIO}) .. ({maxW}, {maxIO})");
    }
}
