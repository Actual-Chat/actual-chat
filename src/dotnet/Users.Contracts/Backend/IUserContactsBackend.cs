namespace ActualChat.Users;

public interface IUserContactsBackend : IComputeService
{
    public Task<UserContact> GetOrCreate(string ownerUserId, string targetUserId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<UserContact?> Get(string contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<UserContact?> Get(string ownerUserId, string targetUserId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<string[]> GetContactIds(string userId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<string> SuggestContactName(string targetUserId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<UserContact?> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] Symbol Id,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<UserContactDiff> Change
    ) : ICommand<UserContact?>;
}
