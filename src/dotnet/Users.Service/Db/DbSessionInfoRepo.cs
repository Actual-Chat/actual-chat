using ActualLab.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public class DbSessionInfoRepo(DbAuthService<UsersDbContext>.Options settings, IServiceProvider services)
    : DbSessionInfoRepo<UsersDbContext, DbSessionInfo, string>(settings, services);
