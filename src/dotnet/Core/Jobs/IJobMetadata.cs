namespace ActualChat.Jobs;

public interface IJobMetadata
{
    string Name { get; }
    Type JobType { get; }
    bool ExecuteAtStart { get; }
    DateTimeOffset GetNextExecutionTime(DateTimeOffset now);
}
