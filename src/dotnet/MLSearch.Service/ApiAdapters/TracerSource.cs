namespace ActualChat.MLSearch.ApiAdapters;

internal interface ITracerSource {
    Tracer GetTracer();
}

internal sealed class TracerSource : ITracerSource
{
    public Tracer GetTracer() => Tracer.Default;
}

internal static class TracerSourceExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TraceRegion? TraceRegion(this ITracerSource? tracing, [CallerMemberName] string label="")
    {
        // This method is made as an extension to handle nulls
        var tracer = tracing?.GetTracer();
        return tracer?.Region(label);
    }
}
