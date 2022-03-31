namespace ActualChat.Users;

public interface IUserContacts
{
    [ComputeMethod]
    public Task<ImmutableArray<UserContact>> GetContacts(Session session, CancellationToken cancellationToken);
}
