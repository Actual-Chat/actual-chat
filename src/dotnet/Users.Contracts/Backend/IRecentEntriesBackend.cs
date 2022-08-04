namespace ActualChat.Users;

public interface IRecentEntriesBackend
{
    [ComputeMethod]
    Task<ImmutableHashSet<string>> List(
        string shardKey,
        RecentScope scope,
        int limit,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<RecentEntry?> Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] string ShardKey,
        [property: DataMember] RecentScope Scope,
        [property: DataMember] string Key,
        [property: DataMember] Moment Date
    ) : ICommand<RecentEntry?>;
}
