namespace ActualChat.Performance;

public interface ITraceSession
{
    TimeSpan Elapsed { get; }
    string Name { get; }
    void Track(string message);
    TraceStep TrackStep(string message);
}
