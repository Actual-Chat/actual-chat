using System;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using Stl.DependencyInjection;

namespace ActualChat.Users.Module
{
    public class UsersDbInitializer : DbInitializer<UsersDbContext>
    {
        public UsersDbInitializer(IServiceProvider services) : base(services) { }
    }
}
