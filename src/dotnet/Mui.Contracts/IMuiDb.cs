namespace ActualChat.Mui;

// Admin-only front for IMuiDbBackend: access checks and result caching
public interface IMuiDb
{
    Task<MuiDatabaseInfo[]> GetDatabases(Session session, CancellationToken cancellationToken);
    // cacheFor <= 0 means no caching
    Task<MuiSqlResult> Query(
        Session session, MuiSqlQuery query, TimeSpan cacheFor, CancellationToken cancellationToken);
    Task ClearCache(Session session, CancellationToken cancellationToken);
}
