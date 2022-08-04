namespace ActualChat.Users;

public interface IRecentEntries : IComputeService
{
    [ComputeMethod]
    Task<ImmutableHashSet<string>> ListUserContactIds(Session session, int limit, CancellationToken cancellationToken);
    [CommandHandler]
    Task<RecentEntry?> Update(UpdateUserContactCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record UpdateUserContactCommand(
        [property: DataMember] Session Session,
        [property: DataMember] string ContactId,
        [property: DataMember] Moment Date
    ) : ISessionCommand<RecentEntry?>;
}
