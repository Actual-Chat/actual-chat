namespace ActualChat.Performance;

public class TracerProvider
{
    public Tracer Tracer { get; init; } = Tracer.Default;

    public TracerProvider() { }
    public TracerProvider(Tracer tracer)
        => Tracer = tracer;
}
