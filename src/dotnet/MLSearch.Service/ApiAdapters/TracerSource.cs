namespace ActualChat.MLSearch.ApiAdapters;

internal interface ITracerSource {
    Tracer GetTracer();
}

internal class TracerSource : ITracerSource
{
    public Tracer GetTracer() => Tracer.Default;
}
