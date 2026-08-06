using ActualChat.Invite.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Invite;

/// <summary>
/// Backend service implementation for managing invite links and activation keys.
/// </summary>
public class InvitesBackend(IServiceProvider services)
    : DbServiceBase<InviteDbContext>(services), IInvitesBackend
{
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
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

        var now = Clocks.SystemClock.Now;
        var nowUtc = now.ToDateTime();
        var dbInvites = await dbContext.Invites
            .Where(x => x.SearchKey == searchKey
                && x.Remaining >= minRemaining
                && (x.ExpiresOn == default || x.ExpiresOn > nowUtc))
            .OrderByDescending(x => x.ExpiresOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var invites = dbInvites.Select(x => x.ToModel()).ToArray();
        var nextExpiresOn = invites
            .Where(x => x.ExpiresOn != default)
            .Min(x => (Moment?)x.ExpiresOn);
        if (nextExpiresOn is { } expiresOn)
            AutoInvalidate(expiresOn, now);
        return invites;
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

        var now = Clocks.SystemClock.Now;
        if (!invite.CanUse(now))
            return null;

        AutoInvalidate(invite.ExpiresOn, now);
        switch (invite) {
        case UserInvite:
            return null;
        case ChatInvite chatInvite: {
            var chatId = chatInvite.ChatId;
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
        case PlaceInvite placeInvite: {
            var place = await PlacesBackend.Get(placeInvite.PlaceId, cancellationToken).ConfigureAwait(false);
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
                _ = PseudoGetAll(invInvite.GetSearchKey());
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
                _ = PseudoGetAll(invInvite.GetSearchKey());
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
        invite = invite.Use(VersionGenerator, Clocks.SystemClock.Now);

        switch (invite) {
        case UserInvite:
            throw StandardError.Constraint("User invites feature is removed.");
        case ChatInvite chatInvite: {
            var chatId = chatInvite.ChatId;
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
        case PlaceInvite placeInvite: {
            await OnUseForPlace(placeInvite.PlaceId).ConfigureAwait(false);
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

        async Task OnUseForChat(ChatId chatId) {
            _ = await ChatsBackend.Get(chatId, cancellationToken).Require().ConfigureAwait(false);

            var dbActivationKey = new DbActivationKey(invite.Id);
            dbContext.Add(dbActivationKey);
            context.Operation.Items.KeylessSet(dbActivationKey.Id);

            var userSettingsUI = Services.UserSettingsUI(session);
            await userSettingsUI.Set(
                ChatInviteSettings.GetKey(chatId),
                new ChatInviteSettings { ActivationKey = dbActivationKey.Id },
                cancellationToken).ConfigureAwait(false);
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
                _ = PseudoGetAll(invInvite.GetSearchKey());
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

    private static void AutoInvalidate(Moment expiresOn, Moment now)
    {
        if (expiresOn != default && expiresOn > now)
            Computed.GetCurrent().Invalidate(expiresOn - now);
    }
}
