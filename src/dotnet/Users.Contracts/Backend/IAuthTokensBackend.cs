namespace ActualChat.Users;

public interface IAuthTokensBackend : IComputeService
{
    Task<AuthToken> Create(Session session, TokenType tokenType, CancellationToken cancellationToken);
    Task<Session> Validate(string token, TokenType tokenType, CancellationToken cancellationToken);
}
