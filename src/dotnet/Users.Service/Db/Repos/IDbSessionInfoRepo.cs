namespace ActualChat.Users.Db;

public interface IDbSessionInfoRepo<TDbSessionInfo, in TDbUserId>
    where TDbSessionInfo : DbSessionInfoBase<TDbUserId>, new()
    where TDbUserId : notnull
{
    public Type SessionInfoEntityType { get; }

    // Write methods
    public Task<TDbSessionInfo> GetOrCreate(
        UsersDbContext dbContext, string sessionId, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo> Upsert(
        UsersDbContext dbContext, string sessionId, SessionInfo sessionInfo, CancellationToken cancellationToken = default);
    public Task<int> Trim(
        DateTime maxLastSeenAt, int maxCount, CancellationToken cancellationToken = default);

    // Read methods
    public Task<TDbSessionInfo?> Get(
        string sessionId, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo?> Get(
        UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default);
    public Task<TDbSessionInfo[]> ListByUser(
        UsersDbContext dbContext, TDbUserId userId, CancellationToken cancellationToken = default);
}
