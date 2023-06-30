namespace ActualChat.Users;

public interface IMobileSessions : IComputeService
{
    [ComputeMethod]
    Task<string> Get(CancellationToken cancellationToken);
}



