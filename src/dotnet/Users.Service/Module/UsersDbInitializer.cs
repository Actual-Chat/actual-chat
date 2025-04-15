using System.Security.Claims;
using ActualChat.Db;
using ActualChat.Users.Db;
using CommunityToolkit.HighPerformance;

namespace ActualChat.Users.Module;

public class UsersDbInitializer(IServiceProvider services) : DbInitializer<UsersDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        await EnsureAdminExists(cancellationToken).ConfigureAwait(false);
        await EnsureSherlockExists(cancellationToken).ConfigureAwait(false);
        if (HostInfo is { IsDevelopmentInstance: true, IsTested: false }) {
            await EnsureTestBotsExist(cancellationToken).ConfigureAwait(false);
            await EnsureTestUsersExist(cancellationToken).ConfigureAwait(false);
        }
        await EnsureSherlockPicUpdated(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task EnsureAdminExists(CancellationToken cancellationToken)
        => await EnsureUserExists(
                new InternalUserInfo(Constants.User.Admin.UserId, Constants.User.Admin.Name) {
                    AvatarBio = "Admin",
                    AvatarPictureUrl = Constants.User.Admin.Picture
                },
                cancellationToken)
            .ConfigureAwait(false);

    private async Task EnsureSherlockExists(CancellationToken cancellationToken)
        => await EnsureUserExists(
                new InternalUserInfo(Constants.User.Sherlock.UserId, Constants.User.Sherlock.Name) {
                    AvatarBio = Constants.User.Sherlock.Name,
                    AvatarMediaId = Constants.User.Sherlock.MediaId
                },
                cancellationToken)
            .ConfigureAwait(false);

    private async Task EnsureUserExists(InternalUserInfo userInfo, CancellationToken cancellationToken)
    {
        var account = await GetInternalAccount(userInfo.Id, cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        Log.LogInformation("Creating '{UserName}' user...", userInfo.UserName);
        await AddInternalAccount(userInfo, cancellationToken).ConfigureAwait(false);
    }

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
                return await AddInternalAccount(new (id, name), cancellationToken).ConfigureAwait(false);
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        Log.LogInformation("Created {Count} test bots", accounts.Length);
    }

    private async Task EnsureTestUsersExist(CancellationToken cancellationToken)
    {
        var account = await GetInternalAccount(new UserId("alberte"), cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        // TODO: test user icons
        InternalUserInfo[] testUsers = [
            new (new ("alberte"),
                "",
                "Albert",
                "Einstein",
                "1-2345678901",
                "albert.einstein@actual.chat"),
            new (new ("spongebob"),
                "",
                "SpongeBob",
                "SquarePants ",
                "1-2345678902",
                "spongebob@actual.chat"),
            new (new ("pelepele"),
                "pele",
                "Edson Arantes",
                "do Nascimento ",
                "1-2345678903",
                "pele@actual.chat",
                "Pelé"),
            new (new ("jalalrumi"),
                "rumi",
                "Jalāl al-Dīn Muḥammad",
                "Rumi",
                "1-2345678904",
                "rumi@actual.chat",
                "Jalāl al-Dīn Muḥammad Rūmī جلال‌الدین محمّد رومی"),
            new (new ("ntesla"),
                "tesla",
                "Nikola",
                "Tesla ",
                "1-2345678905",
                "nikola.tesla@actual.chat",
                "Nikola Tesla"),
        ];
        Log.LogInformation("Creating test users...");
        var accounts = await testUsers
            .Select(async x => {
                Log.LogInformation("+ {UserId}: {UserName}", x.Id, x.UserName);
                return await AddInternalAccount(x, cancellationToken).ConfigureAwait(false);
            })
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        Log.LogInformation("Created {Count} test users", accounts.Length);
    }

    private async Task<AccountFull?> GetInternalAccount(UserId userId, CancellationToken cancellationToken)
    {
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        return await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AccountFull> AddInternalAccount(InternalUserInfo userInfo, CancellationToken cancellationToken)
    {
        var userId = userInfo.Id;
        var userName = userInfo.UserNameOrDefault;
        var commander = Services.Commander();
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();

        var isAdmin = userId == Constants.User.Admin.UserId;
        var userIdentity = new UserIdentity("internal", userId);

        // Create & sign in the user
        var session = Session.New();
        var user = new User(userId, userName).WithIdentity(userIdentity);
        if (!userInfo.FirstName.IsNullOrEmpty())
            user = user.WithClaim(ClaimTypes.GivenName, userInfo.FirstName);
        if (!userInfo.LastName.IsNullOrEmpty())
            user = user.WithClaim(ClaimTypes.Surname, userInfo.LastName);
        if (!userInfo.Phone.IsNullOrEmpty())
            user = user.WithPhone(Phone.Parse(userInfo.Phone));
        if (!userInfo.Email.IsNullOrEmpty())
            user = user.WithClaim(ClaimTypes.Email, userInfo.Email);
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
        var avatarBio = userInfo.AvatarBio.NullIfEmpty() ?? $"I'm just a {userName} test bot";
        var avatarMediaId = userInfo.AvatarMediaId;
        string avatarPictureUrl = "";
        if (avatarMediaId == null)
            avatarPictureUrl = userInfo.AvatarPictureUrl.NullIfEmpty() ??
                $"https://api.dicebear.com/7.x/bottts/svg?seed={userId.Value.GetDjb2HashCode()}";

        var changeAvatarCommand = new Avatars_Change(session, Symbol.Empty, null, new Change<AvatarFull> {
            Create = new AvatarFull(account.Id) {
                Name = userInfo.AvatarNameOrDefault,
                Bio = avatarBio,
                MediaId = avatarMediaId,
                PictureUrl = avatarPictureUrl,
            },
        });
        var avatar = await commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);

        // Set this avatar as the default one
        var serverKvasBackend = Services.GetRequiredService<IServerKvasBackend>();
        var userKvas = serverKvasBackend.GetUserClient(account);
        var userAvatarSettings = new UserAvatarSettings() {
            DefaultAvatarId = avatar.Id,
            AvatarIds = [avatar.Id],
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

    private async Task EnsureSherlockPicUpdated(CancellationToken cancellationToken)
    {
        var dbInitializer = this;
 #pragma warning disable MA0084
        var services = dbInitializer.Services;
 #pragma warning restore MA0084
        var log = services.LogFor(GetType());
        var accountsBackend = services.GetRequiredService<IAccountsBackend>();

        var account = await accountsBackend.Get(Constants.User.Sherlock.UserId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return;

        var avatar = account.Avatar;
        if (avatar.MediaId != null && OrdinalEquals(avatar.Bio, Constants.User.Sherlock.Name))
            return;

        //using var dbContext = dbInitializer.CreateDbContext(true);
        log.LogInformation("Updating Sherlock Avatar");
        var avatarFull = new AvatarFull(account.Id, avatar.Id).WithMissingPropertiesFrom(avatar);
        avatarFull = avatarFull with {
            Bio = Constants.User.Sherlock.Name,
            MediaId = Constants.User.Sherlock.MediaId,
            PictureUrl = "",
        };
        var changeAvatarCommand = new AvatarsBackend_Change(avatar.Id, avatar.Version, Change.Update(avatarFull));
        var commander = services.Commander();
        await commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);
    }

    private sealed record InternalUserInfo(
        UserId Id,
        string UserName = "",
        string FirstName = "",
        string LastName = "",
        string Phone = "",
        string Email = "",
        string AvatarName = "")
    {
        public string AvatarBio { get; init; } = "";
        public MediaId? AvatarMediaId { get; init; }
        public string AvatarPictureUrl { get; init; } = "";

        public string UserNameOrDefault => !UserName.IsNullOrEmpty() ? UserName : $"{FirstName.ToLowerInvariant()}_{LastName.ToLowerInvariant()}";
        public string AvatarNameOrDefault => AvatarName.NullIfEmpty() ?? $"{FirstName} {LastName}".Trim().NullIfEmpty() ?? UserNameOrDefault;
    }
}
