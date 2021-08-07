using System;
using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Generators;

namespace ActualChat.Users.Db
{
    public class DbAppUserRepo : DbUserRepo<UsersDbContext, DbAppUser>
    {
        private static readonly Generator<string> SpeakerIdGenerator =
            new RandomStringGenerator(6 /* for now */, RandomStringGenerator.Base32Alphabet);

        public DbAppUserRepo(DbAuthService<UsersDbContext>.Options options, IServiceProvider services)
            : base(options, services) { }

        public override async Task<DbUser> Create(
            UsersDbContext dbContext,
            User user,
            CancellationToken cancellationToken = default)
        {
            string speakerId;
            lock (SpeakerIdGenerator) {
                speakerId = SpeakerIdGenerator.Next();
            }
            user = user with {
                Identities = user.Identities.SetItem(
                    new UserIdentity("ActualChat.Speaker", speakerId), "")
            };
            var dbUser = new DbAppUser() {
                Name = user.Name,
                SpeakerId = speakerId,
                Claims = user.Claims,
            };
            dbContext.Add(dbUser);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            user = user with { Id = dbUser.Id.ToString() };
            dbUser.UpdateFrom(user);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return dbUser;
        }

    }
}
