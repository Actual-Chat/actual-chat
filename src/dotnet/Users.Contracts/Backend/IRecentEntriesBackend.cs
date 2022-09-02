namespace ActualChat.Users;

public interface IRecentEntriesBackend
{
    [ComputeMethod]
    Task<ImmutableArray<RecentEntry>> List(
        string shardKey,
        RecencyScope scope,
        int limit,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<RecentEntry?> Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] RecencyScope Scope,
        [property: DataMember] string ShardKey,
        [property: DataMember] string Key,
        [property: DataMember] Moment Date
    ) : ICommand<RecentEntry?>, IBackendCommand;
}
