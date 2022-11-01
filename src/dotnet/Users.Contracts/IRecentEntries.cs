namespace ActualChat.Users;

public interface IRecentEntries : IComputeService
{
    [ComputeMethod]
    Task<ImmutableArray<RecentEntry>> ListOwn(
        Session session,
        RecencyScope scope,
        int limit,
        CancellationToken cancellationToken);

    [CommandHandler]
    Task<RecentEntry?> Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] Session Session,
        [property: DataMember] RecencyScope Scope,
        [property: DataMember] string Key,
        [property: DataMember] Moment Date
    ) : ISessionCommand<RecentEntry?>;
}
