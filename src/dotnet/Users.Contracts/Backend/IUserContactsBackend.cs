namespace ActualChat.Users;

public interface IUserContactsBackend
{
    [ComputeMethod]
    public Task<UserContact?> Get(string contactId, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<UserContact?> GetByTargetId(string ownerUserId, string targetUserId, CancellationToken cancellationToken);

    public Task<UserContact> GetOrCreate(string ownerUserId, string targetUserId, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<string[]> GetContactIds(string userId, CancellationToken cancellationToken);

    [ComputeMethod]
    public Task<string> SuggestContactName(string targetUserId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<UserContact> CreateContact(CreateContactCommand command, CancellationToken cancellationToken);

    public record CreateContactCommand(UserContact Contact) : ICommand<UserContact>, IBackendCommand;
}
