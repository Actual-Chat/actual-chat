namespace ActualChat.Users;

public interface IMobileSessions : IComputeService
{
    Task<string> Create(CancellationToken cancellationToken);
    Task<string> Validate(string sessionId, CancellationToken cancellationToken);
}
