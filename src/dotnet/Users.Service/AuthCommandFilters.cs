using ActualChat.Users.Db;
using Cysharp.Text;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Internal;

namespace ActualChat.Users;

public class AuthCommandFilters : DbServiceBase<UsersDbContext>
{
    protected IAuth Auth { get; }
    protected IAuthBackend AuthBackend { get; }
    protected IUserProfilesBackend UserProfilesBackend { get; }
    protected UserNamer UserNamer { get; }
    protected IUserPresences UserPresences { get; }
    protected IDbUserRepo<UsersDbContext, DbUser, string> DbUsers { get; }

    public AuthCommandFilters(IServiceProvider services)
        : base(services)
    {
        Auth = services.GetRequiredService<IAuth>();
        AuthBackend = services.GetRequiredService<IAuthBackend>();
        UserProfilesBackend = services.GetRequiredService<IUserProfilesBackend>();
        UserNamer = services.GetRequiredService<UserNamer>();
        UserPresences = services.GetRequiredService<IUserPresences>();
        DbUsers = services.GetRequiredService<IDbUserRepo<UsersDbContext, DbUser, string>>();
    }

    /// <summary> The filter which clears Sessions.OptionsJson field in the database </summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSignOut(SignOutCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
        if (!Computed.IsInvalidating())
            await ResetSessionOptions().ConfigureAwait(false);

        async Task ResetSessionOptions()
        {
            var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
            await using var __ = dbContext.ConfigureAwait(false);
            var dbSession = await dbContext.Sessions.FirstOrDefaultAsync(x => x.Id == command.Session.Id.Value, cancellationToken)
                .ConfigureAwait(false);
            if (dbSession != null) {
                dbSession.Options = new ImmutableOptionSet();
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary> Takes care of invalidation of IsOnlineAsync once user signs in. </summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task OnSignInMarkOnline(SignInCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        // Invoke command handlers with lower priority
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        if (Computed.IsInvalidating()) {
            var invUserId = context.Operation().Items.Get<string>();
            if (!invUserId.IsNullOrEmpty())
                _ = UserPresences.Get(invUserId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await ResetSessionOptions().ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>(); // Set by default command handler
        if (sessionInfo == null)
            throw Errors.InternalError("No SessionInfo in operation's items.");
        var userId = sessionInfo.UserId;
        var dbUser = await DbUsers.Get(dbContext, userId, true, cancellationToken).ConfigureAwait(false);
        if (dbUser == null)
            return; // Should never happen, but if it somehow does, there is no extra to do in this case


        // Let's try to fix auto-generated user name here
        var newName = await NormalizeName(dbContext, dbUser!.Name, userId, cancellationToken).ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(newName, dbUser.Name)) {
            dbUser.Name = newName;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        context.Operation().Items.Set(dbUser.Id);
        await MarkOnline(userId, cancellationToken).ConfigureAwait(false);

        async Task ResetSessionOptions()
        {
            var dbSession = await dbContext.Sessions
                .FirstOrDefaultAsync(x => x.Id == command.Session.Id.Value, cancellationToken).ConfigureAwait(false);

            if (dbSession != null) {
                dbSession.Options = new ImmutableOptionSet();
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary> Validates user name on edit + makes sure user edits invalidate UserProfiles </summary>
    [CommandHandler(IsFilter = true, Priority = 1)]
    protected virtual async Task OnEditUser(EditUserCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
            var invUser = context.Operation().Items.Get<User>();
            if (invUser?.Name != null)
                _ = UserProfilesBackend.GetByName(invUser.Name, default); // Invalidating old user name
            if (command.Name != null)
                _ = UserProfilesBackend.GetByName(command.Name, default); // Invalidating new user name
            return;
        }
        if (command.Name != null) {
            var error = UserNamer.ValidateName(command.Name);
            if (error != null)
                throw error;

            var user = await Auth.GetUser(command.Session, cancellationToken).ConfigureAwait(false);
            user = user.MustBeAuthenticated();
            var userId = user.Id;

            var dbContext = CreateDbContext();
            await using var _ = dbContext.ConfigureAwait(false);

            var isNameUsed = await dbContext.Users.AsQueryable()
                .AnyAsync(u => u.Name == command.Name && u.Id != userId, cancellationToken)
                .ConfigureAwait(false);
            if (isNameUsed)
                throw new InvalidOperationException("This name is already used by someone else.");

            context.Operation().Items.Set(user); // Let's store old user to invalidate its name
        }

        // Invoke command handler(s) with lower priority
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);
    }

    // Updates online presence state
    [CommandHandler(IsFilter = true, Priority = 1)]
    public virtual async Task SetupSession(
        SetupSessionCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        await context.InvokeRemainingHandlers(cancellationToken).ConfigureAwait(false);

        var sessionInfo = context.Operation().Items.Get<SessionInfo>();
        if (sessionInfo?.IsAuthenticated != true)
            return;
        var userId = sessionInfo.UserId;
        await MarkOnline(userId, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task MarkOnline(string userId, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            var c = Computed.GetExisting(() => UserPresences.Get(userId, default));
            if (c?.IsConsistent() != true)
                return;
            if (c.IsValue(out var v) && v is Presence.Online or Presence.Recording)
                return;
            // We invalidate only when there is a cached value, and it is
            // either false or an error, because the only change that may happen
            // due to sign-in is that this value becomes true.
            _ = UserPresences.Get(userId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var userState = await dbContext.UserPresences.FindAsync(DbKey.Compose(userId), cancellationToken).ConfigureAwait(false);
        if (userState == null) {
            userState = new DbUserPresence() { UserId = userId };
            dbContext.Add(userState);
        }
        userState.OnlineCheckInAt = Clocks.SystemClock.Now;

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> NormalizeName(
        UsersDbContext dbContext,
        string name,
        string userId,
        CancellationToken cancellationToken)
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
        var nextNumber = long.TryParse(nameSuffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number + 1 : 1;
        while (true) {
            var isNameUsed = await dbContext.Users.AsQueryable()
                .AnyAsync(u => u.Name == name && u.Id != userId, cancellationToken)
                .ConfigureAwait(false);
            if (!isNameUsed)
                break;
            name = namePrefix + nextNumber.ToString(CultureInfo.InvariantCulture);
            ++nextNumber;
        }
        return name;
    }
}
