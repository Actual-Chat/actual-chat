namespace ActualChat.MLSearch.ApiAdapters;

internal interface ITracerSource {
    Tracer GetTracer();
}

internal class TracerSource : ITracerSource
{
    public Tracer GetTracer() => Tracer.Default;
}

internal static class TracerSourceExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TraceRegion? TraceRegion(this ITracerSource? tracing)
    {
        // This method is made as an extension to handle nulls
        var tracer = tracing?.GetTracer();
        return tracer?.Region();
    }
}
