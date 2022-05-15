namespace ActualChat.Users;

public interface IUserContacts
{
    [ComputeMethod]
    public Task<ImmutableArray<UserContact>> GetAll(Session session, CancellationToken cancellationToken);
}
