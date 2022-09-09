namespace ActualChat.Jobs;

public interface IJobConfiguration
{
    ICommand<Unit> JobCommand { get; }
    ShardKind ShardKind { get; }
    string ShardKey { get; }
    JobPriority Priority { get; }
}

[DataContract]
public record JobConfiguration<T>(
    [property: DataMember] IJob<T> Job,
    [property: DataMember] ShardKind ShardKind = ShardKind.None,
    [property: DataMember] string ShardKey = "",
    [property: DataMember] JobPriority Priority = JobPriority.Normal) : IJobConfiguration
{
    [IgnoreDataMember]
    public ICommand<Unit> JobCommand => Job;
}

