namespace ActualChat.Users;

public interface IUserContacts : IComputeService
{
    [ComputeMethod]
    public Task<ImmutableArray<UserContact>> List(Session session, CancellationToken cancellationToken);
}
