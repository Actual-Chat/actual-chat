namespace ActualChat.App.Maui;

public static class MauiThreadPoolSettings
{
    public static void Apply()
    {
        var tracer = MauiDiagnostics.Tracer[nameof(MauiThreadPoolSettings)];

        ThreadPool.GetMinThreads(out var minW, out var minIO);
        ThreadPool.GetMaxThreads(out var maxW, out var maxIO);
        var cpuCount = HardwareInfo.ProcessorCount;
        tracer.Point($"{nameof(Apply)} - original settings: ({minW}, {minIO}) .. ({maxW}, {maxIO}), CPU count: {cpuCount}");

        minW = Math.Max(minW, cpuCount + 4);
        minIO = Math.Max(minIO, cpuCount);
        ThreadPool.SetMinThreads(minW, minIO);
        ThreadPool.GetMinThreads(out minW, out minIO);
        tracer.Point($"{nameof(Apply)} - new settings: ({minW}, {minIO}) .. ({maxW}, {maxIO})");
    }
}
