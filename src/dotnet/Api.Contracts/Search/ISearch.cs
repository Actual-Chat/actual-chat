namespace ActualChat.Search;

public interface ISearch : IComputeService
{
    [ComputeMethod(AutoInvalidationDelay = 5)]
    Task<ContactSearchResultPage> FindContacts(
        Session session,
        ContactSearchQuery query,
        CancellationToken cancellationToken);
}
