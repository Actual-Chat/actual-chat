using System;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users.Db
{
    public class DbAppUserByNameResolver : DbEntityResolver<UsersDbContext, string, DbAppUser>
    {
        public DbAppUserByNameResolver(IServiceProvider services)
            : base(new Options() { KeyPropertyName = "Name" }, services)
        { }
    }
}
