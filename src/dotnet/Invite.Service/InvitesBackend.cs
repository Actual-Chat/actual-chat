using System.Security;
using ActualChat.Chat;
using ActualChat.Invite.Backend;
using ActualChat.Invite.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Generators;

namespace ActualChat.Invite;

internal class InvitesBackend : DbServiceBase<InviteDbContext>, IInvitesBackend
{
    private const string InviteIdAlphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static RandomStringGenerator InviteIdGenerator { get; } = new(10, InviteIdAlphabet);

    private readonly ILogger _log;
    private readonly ICommander _commander;
    private readonly IAccounts _accounts;
    private readonly IAccountsBackend _accountsBackend;
    private readonly IChatsBackend _chatsBackend;

    public InvitesBackend(IServiceProvider services) : base(services)
    {
        _log = services.LogFor(GetType());
        _commander = services.Commander();
        _accounts = services.GetRequiredService<IAccounts>();
        _accountsBackend = services.GetRequiredService<IAccountsBackend>();
        _chatsBackend = services.GetRequiredService<IChatsBackend>();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Invite>> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken)
    {
        await PseudoGetAll(searchKey, cancellationToken).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbInvites = await dbContext.Invites
            .Where(x => x.SearchKey == searchKey && x.Remaining >= minRemaining)
            .OrderByDescending(x => x.ExpiresOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbInvites.Select(x => x.ToModel()).ToImmutableArray();
    }

    // [ComputeMethod]
    public virtual async Task<Invite?> Get(string id, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbInvite = await dbContext.Invites
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            .ConfigureAwait(false);
        return dbInvite?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<Invite> Generate(
        IInvitesBackend.GenerateCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invInvite = context.Operation().Items.Get<Invite>();
            if (invInvite != null) {
                _ = PseudoGetAll(invInvite.Details?.GetSearchKey() ?? "", default);
                _ = Get(invInvite.Id, default);
            }
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var expiresOn = command.Invite.ExpiresOn;
        if (expiresOn == Moment.EpochStart)
            expiresOn = Clocks.SystemClock.Now + TimeSpan.FromDays(7);
        var invite = command.Invite with {
            Id = InviteIdGenerator.Next(),
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
            ExpiresOn = expiresOn,
        };
        dbContext.Invites.Add(new DbInvite(invite));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(invite);
        return invite;
    }

    // [CommandHandler]
    public virtual async Task<Invite> Use(
        IInvitesBackend.UseCommand command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Computed.IsInvalidating()) {
            var invInvite = context.Operation().Items.Get<Invite>();
            if (invInvite != null) {
                _ = PseudoGetAll(invInvite.Details?.GetSearchKey() ?? "", default);
                _ = Get(invInvite.Id, default);
            }
            return null!;
        }

        var session = command.Session;
        var account = await _accounts.Get(command.Session, cancellationToken).Require().ConfigureAwait(false);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbInvite = await dbContext.Invites
                .FirstOrDefaultAsync(x => x.Id == command.InviteId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw StandardError.NotFound($"Invite code '{command.InviteId}' is not found.");

        var invite = dbInvite.ToModel();
        invite = invite.Use(VersionGenerator);

        var userInviteDetails = invite.Details?.User;
        if (userInviteDetails != null) {
            if (account.Status == AccountStatus.Suspended)
                throw StandardError.Unauthorized("A suspended account cannot be re-activated via invite code.");
            if (account.IsActive())
                throw StandardError.StateTransition("Your account is already active.");
        }

        var chatInviteDetails = invite.Details?.Chat;
        if (chatInviteDetails != null)
            _ = await _chatsBackend.Get(chatInviteDetails.ChatId, cancellationToken).Require().ConfigureAwait(false);

        dbInvite.UpdateFrom(invite);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation().Items.Set(invite);

        // This piece starts a task that performs the actual invite action once the command completes
        _ = BackgroundTask.Run(async () => {
            // First, let's wait for completion of this pipeline
            await context.OutermostContext.UntypedResultTask.ConfigureAwait(false);
            // If we're here, the command has completed w/o an error

            if (userInviteDetails != null) {
                 var updateCommand = new IAccountsBackend.UpdateCommand(account with {
                     Status = AccountStatus.Active,
                 });
                 await _commander.Call(updateCommand, true, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (chatInviteDetails != null) {
                var updateOptionCommand1 = new ISessionOptionsBackend.UpsertCommand(
                    session,
                    new ("Invite::Id", invite.Id.Value));
                await _commander.Call(updateOptionCommand1, true, cancellationToken).ConfigureAwait(false);
                var updateOptionCommand2 = new ISessionOptionsBackend.UpsertCommand(
                    session,
                    new ("Invite::ChatId", chatInviteDetails.ChatId.Value));
                await _commander.Call(updateOptionCommand2, true, cancellationToken).ConfigureAwait(false);
                return;
            }

            throw StandardError.Constraint("Invite has no details.");
        }, _log, "Invite-specific action failed", cancellationToken);

        return invite;
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoGetAll(string searchKey, CancellationToken cancellationToken)
        => Stl.Async.TaskExt.UnitTask;
}
