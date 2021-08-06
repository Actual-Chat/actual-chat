using System;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db
{
    public class DbAppUserRepo : DbUserRepo<UsersDbContext, DbAppUser>
    {
        public DbAppUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
            : base(options, services) { }
    }
}
