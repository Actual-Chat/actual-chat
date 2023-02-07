namespace ActualChat.Performance;

public interface ITraceSession
{
    string Name { get; }
    void Track(string message);
    TraceStep TrackStep(string message);
}
