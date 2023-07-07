namespace ActualChat.Users;

[Obsolete("Retired in favour of IMobileAuth.")]
public interface IMobileSessions : IComputeService
{
    [Obsolete("Retired in favour of IMobileAuth.")]
    Task<string> Create(CancellationToken cancellationToken);
    [Obsolete("Retired in favour of IMobileAuth.")]
    Task<string> Validate(string sessionId, CancellationToken cancellationToken);
}
