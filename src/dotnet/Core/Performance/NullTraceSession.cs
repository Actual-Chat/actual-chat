namespace ActualChat.Performance;

public sealed class NullTraceSession : ITraceSession
{
    public static readonly NullTraceSession Instance = new ();

    private NullTraceSession()
    {
    }

    public TimeSpan Elapsed  => TimeSpan.Zero;

    public string Name => "Null";

    public void Track(string message)
    {
    }

    public TraceStep TrackStep(string message)
        => new TraceStep(this, message);
}
