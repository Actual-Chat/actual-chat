namespace ActualChat.Users;

public interface IUserContacts : IComputeService
{
    [ComputeMethod]
    public Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken);
}
