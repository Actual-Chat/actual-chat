using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.Db;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users
{
    public class SpeakerService : DbServiceBase<UsersDbContext>, ISpeakerService
    {
        protected IServerSideAuthService Auth { get; }
        protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }
        protected IDbEntityResolver<string, DbUser> DbUserResolver { get; }
        protected DbUserByNameResolver DbUserByNameResolver { get; }
        protected ISpeakerNameService SpeakerNames { get; }

        public SpeakerService(IServiceProvider services) : base(services)
        {
            Auth = services.GetRequiredService<IServerSideAuthService>();
            DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
            DbUserResolver = services.DbEntityResolver<string, DbUser>();
            DbUserByNameResolver = services.GetRequiredService<DbUserByNameResolver>();
            SpeakerNames = services.GetRequiredService<ISpeakerNameService>();
        }

        public virtual async Task<Speaker?> TryGet(string userId, CancellationToken cancellationToken = default)
        {
            var dbUser = await DbUserResolver.TryGet(userId, cancellationToken);
            if (dbUser == null)
                return null;
            return new Speaker(dbUser.Id, dbUser.Name);
        }

        public virtual async Task<Speaker?> TryGetByName(string name, CancellationToken cancellationToken = default)
        {
            var dbUser = await DbUserByNameResolver.TryGet(name, cancellationToken);
            if (dbUser == null)
                return null;
            return new Speaker(dbUser.Id, dbUser.Name);
        }
    }
}
