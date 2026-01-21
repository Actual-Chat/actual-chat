namespace ActualChat.Users.Db;

public class DbSessionInfoRepo(AuthBackend.Options settings, IServiceProvider services)
    : DbSessionInfoRepoBase<DbSessionInfo, string>(settings, services);
