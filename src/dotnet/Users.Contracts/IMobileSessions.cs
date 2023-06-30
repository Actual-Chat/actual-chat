namespace ActualChat.Users;

public interface IMobileSessions : IComputeService
{
    [ComputeMethod]
    Task<string> Create(CancellationToken cancellationToken);

    [ComputeMethod]
    Task<string> Validate(string sessionId, CancellationToken cancellationToken);
}



