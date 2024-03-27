using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Db;
using Microsoft.Toolkit.HighPerformance;

namespace ActualChat.Users.Module;

public class UsersDbInitializer(IServiceProvider services) : DbInitializer<UsersDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        await EnsureAdminExists(cancellationToken).ConfigureAwait(false);
        await EnsureMLSearchBotExists(cancellationToken).ConfigureAwait(false);
        if (HostInfo is { IsDevelopmentInstance: true, IsTested: false }) 
            await EnsureTestBotsExist(cancellationToken).ConfigureAwait(false);

    }

    // Private methods

    private async Task EnsureAdminExists(CancellationToken cancellationToken)
    => await EnsureUserExists(Constants.User.Admin.UserId, Constants.User.Admin.Name, cancellationToken)
        .ConfigureAwait(false);
    private async Task EnsureUserExists(UserId userId, string name, CancellationToken cancellationToken)
    {
        var account = await GetInternalAccount(userId, cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        Log.LogInformation($"Creating {name} user...");
        await AddInternalAccount(userId, name, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureMLSearchBotExists(CancellationToken cancellationToken)
    => await EnsureUserExists(
            Constants.User.MLSearchBot.UserId, 
            Constants.User.MLSearchBot.Name, 
            cancellationToken
        )
        .ConfigureAwait(false);

    private async Task EnsureTestBotsExist(CancellationToken cancellationToken)
    {
        var account = await GetInternalAccount(new UserId("testbot0"), cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        Log.LogInformation("Creating test bots...");
        var accounts = await Enumerable
            .Range(0, Constants.User.TestBotCount)
            .Select(async i => {
                var id = new UserId($"testbot{i}");
                var name = $"Robo {RandomNameGenerator.Default.Generate()}";
                Log.LogInformation("+ {UserId}: {UserName}", id, name);
                return await AddInternalAccount(id, name, cancellationToken).ConfigureAwait(false);
            })
            .Collect()
            .ConfigureAwait(false);
        Log.LogInformation("Created {Count} test bots", accounts.Length);
    }

    private async Task<AccountFull?> GetInternalAccount(UserId userId, CancellationToken cancellationToken)
    {
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        return account;
    }

    private async Task<AccountFull> AddInternalAccount(UserId userId, string name, CancellationToken cancellationToken)
    {
        var commander = Services.Commander();
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();

        var isAdmin = userId == Constants.User.Admin.UserId;
        var userIdentity = new UserIdentity("internal", userId);

        // Create & sign in the user
        var session = Session.New();
        var user = new User(userId, name).WithIdentity(userIdentity);
        var signInCommand = new AuthBackend_SignIn(session, user, userIdentity);
        await commander.Call(signInCommand, cancellationToken).ConfigureAwait(false);

        // Fetch its account
        var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require();
        if (account.Status != AccountStatus.Active) {
            account = account with { Status = AccountStatus.Active };
            var updateCommand = new AccountsBackend_Update(account, account.Version);
            await commander.Call(updateCommand, cancellationToken).ConfigureAwait(false);
            account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        }
        account.Require(isAdmin ? AccountFull.MustBeAdmin : AccountFull.MustBeActive);

        // Create avatar
        var avatarBio = isAdmin ? "Admin" : $"I'm just a {name} test bot";
        var avatarPicture = isAdmin
            ? Constants.User.Admin.Picture
            : $"https://api.dicebear.com/7.x/bottts/svg?seed={userId.Value.GetDjb2HashCode()}";
        var changeAvatarCommand = new Avatars_Change(session, Symbol.Empty, null, new Change<AvatarFull> {
            Create = new AvatarFull(account.Id) {
                Name = name,
                Bio = avatarBio,
                PictureUrl = avatarPicture,
            },
        });
        var avatar = await commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);

        // Set this avatar as the default one
        var serverKvasBackend = Services.GetRequiredService<IServerKvasBackend>();
        var userKvas = serverKvasBackend.GetUserClient(account);
        var userAvatarSettings = new UserAvatarSettings() {
            DefaultAvatarId = avatar.Id,
            AvatarIds = ApiArray.New(avatar.Id),
        };
        await userKvas.SetUserAvatarSettings(userAvatarSettings, cancellationToken).ConfigureAwait(false);

        // Fetch the final account + do some final checks
        account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
        account.Require(isAdmin ? AccountFull.MustBeAdmin : AccountFull.MustBeActive);
        if (account.Status != AccountStatus.Active)
            throw StandardError.Internal("Wrong account status.");
        if (account.Avatar.Require().Id != avatar.Id)
            throw StandardError.Internal("Wrong avatar ID.");

        return account;
    }
}
