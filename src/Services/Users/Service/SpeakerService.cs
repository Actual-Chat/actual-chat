using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Async;
using Stl.CommandR;
using Stl.CommandR.Configuration;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Text;

namespace ActualChat.Users
{
    public class SpeakerService : DbServiceBase<UsersDbContext>, ISpeakerService
    {
        protected IServerSideAuthService AuthService { get; }
        protected IDbUserRepo<UsersDbContext> DbUserRepo { get; }
        protected DbAppUserBySpeakerIdResolver DbAppUserBySpeakerIdResolver { get; }
        protected DbAppUserByNameResolver DbAppUserByNameResolver { get; }
        protected ISpeakerNameService SpeakerNameService { get; }

        public SpeakerService(IServiceProvider services) : base(services)
        {
            AuthService = services.GetRequiredService<IServerSideAuthService>();
            DbUserRepo = services.GetRequiredService<IDbUserRepo<UsersDbContext>>();
            DbAppUserBySpeakerIdResolver = services.GetRequiredService<DbAppUserBySpeakerIdResolver>();
            DbAppUserByNameResolver = services.GetRequiredService<DbAppUserByNameResolver>();
            SpeakerNameService = services.GetRequiredService<ISpeakerNameService>();
        }

        public virtual async Task<Speaker?> TryGet(string speakerId, CancellationToken cancellationToken = default)
        {
            var dbAppUser = await DbAppUserBySpeakerIdResolver.TryGet(speakerId, cancellationToken);
            if (dbAppUser == null)
                return null;
            return new Speaker(dbAppUser.SpeakerId, dbAppUser.Name);
        }

        public virtual async Task<Speaker?> TryGetByName(string name, CancellationToken cancellationToken = default)
        {
            var dbAppUser = await DbAppUserByNameResolver.TryGet(name, cancellationToken);
            if (dbAppUser == null)
                return null;
            return new Speaker(dbAppUser.SpeakerId, dbAppUser.Name);
        }


        // Validates user name on edit
        [CommandHandler(IsFilter = true, Priority = 1)]
        protected virtual async Task OnEditUser(EditUserCommand command, CancellationToken cancellationToken)
        {
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                await context.InvokeRemainingHandlers(cancellationToken);
                if (command.Name != null)
                    TryGetByName(command.Name, default).Ignore();
                return;
            }
            if (command.Name != null) {
                var error = SpeakerNameService.ValidateName(command.Name);
                if (error != null)
                    throw error;

                var user = await AuthService.GetUser(command.Session, cancellationToken);
                user = user.MustBeAuthenticated();
                var userId = long.Parse(user.Id);

                await using var dbContext = CreateDbContext();
                if (dbContext.Users.Any(u => u.Name == command.Name && u.Id != userId))
                    throw new InvalidOperationException("This name is already used by someone else.");
            }
            await context.InvokeRemainingHandlers(cancellationToken);
        }
    }
}
