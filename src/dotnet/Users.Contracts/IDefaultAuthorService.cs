namespace ActualChat.Users;

/// <summary>
/// Returns source author properties for the user (without any overrides) or generates them.
/// </summary>
// TODO: make facade/ internal network client for the IDefaultAuthorService (?)
public interface IDefaultAuthorService
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<IAuthorInfo> Get(UserId userId, CancellationToken cancellationToken);
}
