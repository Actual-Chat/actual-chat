namespace ActualChat.Users;

public interface IUserContacts : IComputeService
{
    [ComputeMethod]
    public Task<ImmutableArray<UserContact>> List(Session session, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<UserContact?> Get(Session session, string contactId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<UserContact?> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Session Session,
        [property: DataMember] Symbol Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<UserContactDiff> Change
    ) : ISessionCommand<UserContact?>;
}
