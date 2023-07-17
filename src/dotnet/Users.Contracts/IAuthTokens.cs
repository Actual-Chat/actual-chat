namespace ActualChat.Users;

public interface IAuthTokens: IComputeService
{
    Task<AuthToken> Create(Session session, TokenType tokenType, CancellationToken cancellationToken);
}
