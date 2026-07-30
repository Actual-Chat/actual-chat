using ActualChat.Db;
using ActualChat.Users.Db;

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
                    AvatarPictureUrl = Constants.User.Admin.Picture,
                },
                cancellationToken)
            .ConfigureAwait(false);

    private async Task EnsureSherlockExists(CancellationToken cancellationToken)
        => await EnsureUserExists(
                new InternalUserInfo(Constants.User.Sherlock.UserId, Constants.User.Sherlock.Name) {
                    AvatarBio = Constants.User.Sherlock.Name,
                    AvatarMediaId = Constants.User.Sherlock.MediaId,
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
        var account = await GetInternalAccount(UserId.Parse("testbot0"), cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        Log.LogInformation("Creating test bots...");
        var accounts = await Enumerable
            .Range(0, Constants.User.TestBotCount)
            .Select(async i => {
                var id = UserId.Parse($"testbot{i}");
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
        var account = await GetInternalAccount(UserId.Parse("alberte"), cancellationToken).ConfigureAwait(false);
        if (account != null)
            return;

        // TODO: test user icons
        // Phone numbers use the NANPA-reserved 555-01XX range (fictional/testing use)
        InternalUserInfo[] testUsers = [
            new (UserId.Parse("alberte"),
                "",
                "Albert",
                "Einstein",
                "1-8005550101",
                $"albert.einstein{Constants.Team.EmailSuffix}"),
            new (UserId.Parse("spongebob"),
                "",
                "SpongeBob",
                "SquarePants ",
                "1-8005550102",
                $"spongebob{Constants.Team.EmailSuffix}"),
            new (UserId.Parse("pelepele"),
                "pele",
                "Edson Arantes",
                "do Nascimento ",
                "1-8005550103",
                $"pele{Constants.Team.EmailSuffix}",
                "Pelé"),
            new (UserId.Parse("jalalrumi"),
                "rumi",
                "Jalāl al-Dīn Muḥammad",
                "Rumi",
                "1-8005550104",
                $"rumi{Constants.Team.EmailSuffix}",
                "Jalāl al-Dīn Muḥammad Rūmī جلال‌الدین محمّد رومی"),
            new (UserId.Parse("ntesla"),
                "tesla",
                "Nikola",
                "Tesla ",
                "1-8005550105",
                $"nikola.tesla{Constants.Team.EmailSuffix}",
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

    private Task<AccountFull> AddInternalAccount(InternalUserInfo userInfo, CancellationToken cancellationToken)
        => InternalAccounts.Create(Services, userInfo, cancellationToken);

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
        if (avatar.MediaId != null && avatar.Bio == Constants.User.Sherlock.Name)
            return;

        //using var dbContext = dbInitializer.CreateDbContext(true);
        log.LogInformation("Updating Sherlock Avatar");
        var changeAvatarCommand = new AvatarsBackend_Change(avatar.Id, avatar.Version,
            Change.Update(new AvatarDiff {
                Bio = Constants.User.Sherlock.Name,
                MediaId = Option.Some<MediaId?>(Constants.User.Sherlock.MediaId),
                PictureUrl = "",
            }));
        var commander = services.Commander();
        await commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);
    }
}
