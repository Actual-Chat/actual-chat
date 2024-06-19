using ActualLab.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public class DbSessionInfoRepo(DbAuthService<UsersDbContext>.Options settings, IServiceProvider services)
    : DbSessionInfoRepo<UsersDbContext, DbSessionInfo, string>(settings, services){
    public override async Task<DbSessionInfo?> Get(UsersDbContext dbContext, string sessionId, bool forUpdate, CancellationToken cancellationToken = default) {
        using var _activity = AppDiagnostics.AppTrace.StartActivity("DbSessionInfoRepo:Get");
        return await base.Get(dbContext, sessionId, forUpdate, cancellationToken).ConfigureAwait(false);
    }
}
