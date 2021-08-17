using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Users.Db;
using Cysharp.Text;
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
using Stl.Fusion.Operations;

namespace ActualChat.Users
{
    public class AuthServiceCommandFilters : DbServiceBase<UsersDbContext>
    {
        protected IServerSideAuthService Auth { get; }
        protected ISpeakerService Speakers { get; }
        protected ISpeakerNameService SpeakerNames { get; }
        protected ISpeakerStateService SpeakerStates { get; }
        protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }

        public AuthServiceCommandFilters(IServiceProvider services)
            : base(services)
        {
            Auth = services.GetRequiredService<IServerSideAuthService>();
            Speakers = services.GetRequiredService<ISpeakerService>();
            SpeakerNames = services.GetRequiredService<ISpeakerNameService>();
            SpeakerStates = services.GetRequiredService<ISpeakerStateService>();
            DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
        }

        // Takes care of invalidation of IsOnlineAsync once user signs in
        [CommandHandler(IsFilter = true, Priority = 1)]
        public virtual async Task OnSignIn(SignInCommand command, CancellationToken cancellationToken)
        {
            var context = CommandContext.GetCurrent();

            // Invoke command handler(s) with lower priority
            await context.InvokeRemainingHandlers(cancellationToken);

            if (Computed.IsInvalidating()) {
                var invSpeaker = context.Operation().Items.TryGet<Speaker>();
                if (invSpeaker != null)
                    SpeakerStates.IsOnline(invSpeaker.Id, default).Ignore();
                return;
            }

            // Let's try to fix auto-generated user name here
            await using var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
            var userId = sessionInfo.UserId;
            var dbUser = await DbUsers.TryGet(dbContext, userId, cancellationToken);
            if (dbUser == null)
                return; // Should never happen, but if it somehow does, there is no extra to do in this case
            var newName = await NormalizeName(dbContext, dbUser!.Name, userId, cancellationToken);
            if (newName != dbUser.Name) {
                dbUser.Name = newName;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            var speaker = new Speaker(dbUser.Id, dbUser.Name);
            context.Operation().Items.Set(speaker);
        }


        // Validates user name on edit
        [CommandHandler(IsFilter = true, Priority = 1)]
        protected virtual async Task OnEditUser(EditUserCommand command, CancellationToken cancellationToken)
        {
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                await context.InvokeRemainingHandlers(cancellationToken);
                if (command.Name != null)
                    Speakers.TryGetByName(command.Name, default).Ignore();
                return;
            }
            if (command.Name != null) {
                var error = SpeakerNames.ValidateName(command.Name);
                if (error != null)
                    throw error;

                var user = await Auth.GetUser(command.Session, cancellationToken);
                user = user.MustBeAuthenticated();
                var userId = user.Id;

                await using var dbContext = CreateDbContext();
                var isNameUsed = await dbContext.Users.AsQueryable()
                    .AnyAsync(u => u.Name == command.Name && u.Id != userId, cancellationToken);
                if (isNameUsed)
                    throw new InvalidOperationException("This name is already used by someone else.");
            }

            // Invoke command handler(s) with lower priority
            await context.InvokeRemainingHandlers(cancellationToken);
        }

        // Private methods

        private async Task<string> NormalizeName(
            UsersDbContext dbContext,
            string name,
            string userId,
            CancellationToken cancellationToken = default)
        {
            // Normalizing name
            using var sb = ZString.CreateStringBuilder();
            foreach (var c in name) {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else if (sb.Length == 0 || char.IsLetterOrDigit(sb.AsSpan()[^1]))
                    sb.Append('_');
            }
            name = sb.ToString();
            if (name.Length < 4 || !char.IsLetter(name[0]))
                name = "user-" + name;

            // Finding the number @ the tail
            var numberStartIndex = name.Length;
            for (; numberStartIndex >= 1; numberStartIndex--) {
                if (!char.IsNumber(name[numberStartIndex - 1]))
                    break;
            }

            // Iterating through these tail numbers to get the unique user name
            var namePrefix = name.Substring(0, numberStartIndex);
            var nameSuffix = name.Substring(numberStartIndex);
            var nextNumber = long.TryParse(nameSuffix, out var number) ? number + 1 : 1;
            while (true) {
                var isNameUsed = await dbContext.Users.AsQueryable()
                    .AnyAsync(u => u.Name == name && u.Id != userId, cancellationToken);
                if (!isNameUsed)
                    break;
                name = namePrefix + nextNumber++;
            }
            return name;
        }
    }
}
