using ActualChat.Chat;
using ActualChat.Invite.Db;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Invite;

public class InvitesBackend(IServiceProvider services)
    : DbServiceBase<InviteDbContext>(services), IInvitesBackend
{
    [field: AllowNull, MaybeNull]
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    [field: AllowNull, MaybeNull]
    private IServerKvas ServerKvas => field ??= Services.ServerKvas();
    private IDbEntityResolver<string, DbInvite> DbInviteResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbInvite>>();
    private IDbEntityResolver<string, DbActivationKey> DbActivationKeyResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbActivationKey>>();

    // [ComputeMethod]
    public virtual async Task<Invite?> Get(string id, CancellationToken cancellationToken)
    {
        var dbInvite = await DbInviteResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbInvite?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<Invite[]> GetAll(string searchKey, int minRemaining, CancellationToken cancellationToken)
    {
        await PseudoGetAll(searchKey).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbInvites = await dbContext.Invites
            .Where(x => x.SearchKey == searchKey && x.Remaining >= minRemaining)
            .OrderByDescending(x => x.ExpiresOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbInvites.Select(x => x.ToModel()).ToArray();
    }

    // [ComputeMethod]
    public virtual async Task<bool> IsValid(string activationKey, CancellationToken cancellationToken)
    {
        var dbActivationKey = await DbActivationKeyResolver.Get(activationKey, cancellationToken).ConfigureAwait(false);
        return dbActivationKey != null;
    }

    // [ComputeMethod]
    public virtual async Task<InviteChatLinkPreview?> GetInviteChatLinkPreview(
        UserId accountId,
        string inviteId,
        CancellationToken cancellationToken)

    {
        var invite = await Get(inviteId, cancellationToken).ConfigureAwait(false);
        if (invite is null)
            return null;

        if (!invite.CanUse())
            return null;

        switch (invite.Details.Option) {
        case UserInviteOption:
            return null;
        case ChatInviteOption chatInviteOption: {
            var chatId = chatInviteOption.ChatId;
            var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null)
                return null;

            if (chatId is PlaceChatId placeChatId) {
                if (placeChatId.IsRoot)
                    return null;

                var placeId = placeChatId.PlaceId;
                var place = await PlacesBackend.Get(placeId, cancellationToken).ConfigureAwait(false);
                if (chat is { IsPublic: true })
                    return new InviteChatLinkPreview(chat, place);

                var placeRules = await ChatsBackend
                    .GetRules(placeId.RootChatId, accountId, cancellationToken)
                    .ConfigureAwait(false);
                if (placeRules.IsMember())
                    return new InviteChatLinkPreview(chat, place);

                return null;
            }
            return new InviteChatLinkPreview(chat, null);
        }
        case PlaceInviteOption placeInviteOption: {
            var place = await PlacesBackend.Get(placeInviteOption.PlaceId, cancellationToken).ConfigureAwait(false);
            return new InviteChatLinkPreview(null, place);
        }
        default:
            throw StandardError.Format<Invite>();
        }
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnGenerate(
        InvitesBackend_Generate command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invInvite = context.Operation.Items.KeylessGet<Invite>();
            if (invInvite != null) {
                _ = PseudoGetAll(invInvite.Details?.GetSearchKey() ?? "");
                _ = Get(invInvite.Id, default);
            }
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var expiresOn = command.Invite.ExpiresOn;
        if (expiresOn == default)
            expiresOn = Clocks.SystemClock.Now + Constants.Invites.Defaults.ExpiresIn;
        var invite = command.Invite with {
            Id = DbInvite.IdGenerator.Next(),
            Version = VersionGenerator.NextVersion(),
            CreatedAt = Clocks.SystemClock.Now,
            ExpiresOn = expiresOn,
        };
        dbContext.Invites.Add(new DbInvite(invite));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(invite);
        return invite;
    }

    // [CommandHandler]
    public virtual async Task<Invite> OnUse(
        InvitesBackend_Use command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invInvite = context.Operation.Items.KeylessGet<Invite>();
            if (invInvite != null) {
                _ = PseudoGetAll(invInvite.Details?.GetSearchKey() ?? "");
                _ = Get(invInvite.Id, default);
            }
            var invActivationKey = context.Operation.Items.KeylessGet<string>();
            if (invActivationKey != null)
                _ = IsValid(invActivationKey, default);
            return default!;
        }

        var session = command.Session;
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbInvite = await dbContext.Invites
                .FirstOrDefaultAsync(x => x.Id == command.InviteId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw StandardError.NotFound<Invite>("Invite with the specified code is not found.");

        var invite = dbInvite.ToModel();
        invite = invite.Use(VersionGenerator);

        switch (invite.Details.Option) {
        case UserInviteOption: {
            if (account.IsGuestOrNull())
                throw StandardError.Unauthorized("Please sign in and open this link again to use this invite.");
            if (account.Status == AccountStatus.Suspended)
                throw StandardError.Unauthorized("A suspended account cannot be re-activated via invite code.");
            if (account.IsActive())
                throw StandardError.StateTransition("Your account is already active.");

            // Raise events
            var activatedAccount = account with { Status = AccountStatus.Active };
            context.Operation.AddEvent(new AccountsBackend_Update(activatedAccount, null));
            break;
        }
        case ChatInviteOption chatInviteOption: {
            var chatId = chatInviteOption.ChatId;
            if (chatId is PlaceChatId { IsRoot: false } placeChatId) {
                var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
                var placeId = placeChatId.PlaceId;
                var placeRules = await ChatsBackend
                    .GetRules(placeId.RootChatId, account.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (chat is { IsPublic: true })
                    await OnUseForPlace(placeId).ConfigureAwait(false); // Activate read permission to the place.
                else if (placeRules.IsMember())
                    await OnUseForChat(chatId).ConfigureAwait(false);  // Activate read permission to private place chat.
                else
                    throw StandardError.Constraint("Only place members can use this code.");
            }
            else
                await OnUseForChat(chatId).ConfigureAwait(false);
            break;
        }
        case PlaceInviteOption placeInviteOption: {
            await OnUseForPlace(placeInviteOption.PlaceId).ConfigureAwait(false);
            break;
        }
        default:
            throw StandardError.Format<Invite>();
        }
        dbInvite.UpdateFrom(invite);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(invite);
        return invite;

        Task OnUseForPlace(PlaceId placeId)
            => OnUseForChat(placeId.RootChatId);

        async Task OnUseForChat(ChatId chatId)
        {
            _ = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);

            var dbActivationKey = new DbActivationKey(invite.Id);
            dbContext.Add(dbActivationKey);
            context.Operation.Items.KeylessSet(dbActivationKey.Id);

            var accountSettings = new AccountSettings(ServerKvas, session);
            await accountSettings
                .Set(ServerKvasInviteKey.ForChat(chatId), dbActivationKey.Id, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // [CommandHandler]
    public virtual async Task OnRevoke(
        InvitesBackend_Revoke command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invInvite = context.Operation.Items.KeylessGet<Invite>();
            if (invInvite != null) {
                _ = PseudoGetAll(invInvite.Details?.GetSearchKey() ?? "");
                _ = Get(invInvite.Id, default);
            }
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbInvite = await dbContext.Invites
                .FirstOrDefaultAsync(x => x.Id == command.InviteId, cancellationToken)
                .ConfigureAwait(false)
            ?? throw StandardError.NotFound<Invite>("Invite with the specified code is not found.");

        var invite = dbInvite.ToModel();
        invite = invite.Revoke(VersionGenerator);
        dbInvite.UpdateFrom(invite);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.Operation.Items.KeylessSet(invite);
    }

    [ComputeMethod]
    protected virtual Task<Unit> PseudoGetAll(string searchKey)
        => ActualLab.Async.TaskExt.UnitTask;
}
