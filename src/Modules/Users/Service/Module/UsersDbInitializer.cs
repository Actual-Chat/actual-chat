using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Users.Module
{
    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class UsersDbInitializer : DbInitializer<UsersDbContext>
    {
        public UsersDbInitializer(IServiceProvider services) : base(services) { }
    }
}
