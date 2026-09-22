using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Security;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class WebHooksBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IWebHooksBackend
{
    private const string HeaderTokenSymbols = "!#$%&'*+-.^_`|~";
    private static readonly HashSet<string> ReservedHeaderNames = new(StringComparer.OrdinalIgnoreCase) {
        "webhook-id", "webhook-timestamp", "webhook-signature", "user-agent", "content-type",
        "host", "content-length", "transfer-encoding", "connection",
    };

    private WebHookSecrets Secrets => field ??= Services.GetRequiredService<WebHookSecrets>();
    private WebHookPayloads Payloads => field ??= Services.GetRequiredService<WebHookPayloads>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend => field ??= Services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbWebHook> DbWebHookResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbWebHook>>();
    private WebHookDeliverer Deliverer => field ??= Services.GetRequiredService<WebHookDeliverer>();
    private WebHookInbox WebHookInbox => field ??= Services.GetRequiredService<WebHookInbox>();
    private EgressGuard EgressGuard => field ??= Services.GetRequiredService<EgressGuard>();
    private FlowHub FlowHub => field ??= Services.FlowHub();
    private HostInfo HostInfo => field ??= Services.HostInfo();

    // [ComputeMethod]
    public virtual async Task<WebHook?> Get(WebHookId id, CancellationToken cancellationToken)
    {
        var dbWebHook = await DbWebHookResolver.Get(id.Value, cancellationToken).ConfigureAwait(false);
        return dbWebHook?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHook>> ListByScope(
        WebHookScope scope,
        string scopeId,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbWebHooks = await dbContext.WebHooks
            .Where(x => x.ScopeId == scopeId && x.Scope == scope)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbWebHooks.Select(x => x.ToModel()).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHook>> ListActiveForChat(ChatId chatId, CancellationToken cancellationToken)
    {
        IEnumerable<WebHook> hooks = await ListByScope(WebHookScope.Chat, chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (chatId is PlaceChatId placeChatId) {
            var placeHooks = await ListByScope(WebHookScope.Place, placeChatId.PlaceId.Value, cancellationToken)
                .ConfigureAwait(false);
            hooks = hooks.Concat(placeHooks);
        }
        return hooks.Where(x => x.IsActiveOutgoing && x.Covers(chatId)).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHook>> ListActiveForUser(UserId userId, CancellationToken cancellationToken)
    {
        var hooks = await ListByScope(WebHookScope.User, userId.Value, cancellationToken).ConfigureAwait(false);
        return hooks.Where(x => x.IsActiveOutgoing).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHook>> ListByCreator(UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var createdBy = userId.Value;
        var dbWebHooks = await dbContext.WebHooks
            .Where(x => x.CreatedBy == createdBy)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbWebHooks.Select(x => x.ToModel()).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<WebHook?> GetByTokenHash(string tokenHash, CancellationToken cancellationToken)
    {
        if (tokenHash.IsNullOrEmpty())
            return null;

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbWebHook = await dbContext.WebHooks
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.Kind == WebHookKind.Incoming, cancellationToken)
            .ConfigureAwait(false);
        return dbWebHook?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasUserScopedHooks(CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        return await dbContext.WebHooks
            .AnyAsync(x => x.Scope == WebHookScope.User && x.Kind == WebHookKind.Outgoing && x.IsEnabled,
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<WebHookDelivery>> ListDeliveries(
        WebHookId id,
        int limit,
        CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbDeliveries = await dbContext.WebHookDeliveries
            .Where(x => x.WebHookId == id.Value)
            .OrderByDescending(x => x.Seq)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbDeliveries.Select(x => x.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<WebHookChangeResult> OnChange(
        WebHooksBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (scope, scopeId, id, expectedVersion, change, changedBy) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            InvalidateHook(context);
            _ = HasUserScopedHooks(default);
            if (change.IsRemove() && context.Operation.Items.KeylessGet<WebHook>() is { } removedHook)
                _ = ListDeliveries(removedHook.Id, Constants.WebHooks.DeliveryListLimit, default);
            return default!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        string? secret = null;
        var dbWebHook = id is null
            ? null
            : await dbContext.WebHooks
                .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                .ConfigureAwait(false);
        if (change.IsCreate(out var createDiff)) {
            if (dbWebHook is not null)
                throw StandardError.Constraint("A web hook with this id already exists.");

            var kind = createDiff.Kind ?? WebHookKind.Outgoing;
            if (kind == WebHookKind.Incoming && scope != WebHookScope.Chat)
                throw StandardError.Constraint("Incoming web hooks are chat-scoped.");

            var webHookId = id ?? WebHookId.New();
            if (kind == WebHookKind.Incoming) {
                // The bot's user id is derived from the hook id, so a create may only mint a new bot
                // or pick up a live one left by its own earlier attempt. Anything else - the retired
                // bot of a deleted hook, or an unrelated account whose id happens to match - is off
                // limits: EnsureBot would otherwise turn that account into this chat's bot.
                var botAccount = await AccountsBackend
                    .Get(webHookId.ToBotUserId(), cancellationToken)
                    .ConfigureAwait(false);
                if (botAccount is not (null or { IsBot: true, Status: AccountStatus.Active }))
                    throw StandardError.Constraint("This web hook id is already taken.");
            }

            var webHook = new WebHook(webHookId, VersionGenerator.NextVersion()) {
                Scope = scope,
                ScopeId = scopeId,
                Kind = kind,
                CreatedBy = changedBy,
                CreatedAt = now,
                ModifiedAt = now,
            }.ApplyDiff(createDiff);
            Validate(webHook, createDiff);
            if (kind == WebHookKind.Incoming) {
                secret = WebHookTokens.New();
                dbWebHook = new DbWebHook(webHook) { TokenHash = WebHookTokens.Hash(secret) };
                await EnsureBot(webHook, ChatId.Parse(scopeId), createDiff, cancellationToken).ConfigureAwait(false);
                AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
            }
            else {
                secret = StandardWebhookSigner.NewSecret();
                dbWebHook = new DbWebHook(webHook) { SecretProtected = Secrets.Protect(secret) };
                ApplyCustomHeader(dbWebHook, createDiff);
            }
            dbContext.Add(dbWebHook);
        }
        else if (change.IsUpdate(out var updateDiff)) {
            var existing = dbWebHook.Require().ToModel().RequireVersion(expectedVersion);
            if (updateDiff.Kind is { } newKind && newKind != existing.Kind)
                throw StandardError.Constraint("Web hook kind cannot be changed.");

            var webHook = existing.ApplyDiff(updateDiff) with {
                ModifiedAt = now,
                Version = VersionGenerator.NextVersion(dbWebHook.Version),
            };
            if (updateDiff.IsEnabled == true) {
                webHook = webHook with { DisabledReason = WebHookDisabledReason.None, ConsecutiveFailures = 0 };
                // Whatever was left pending by a manual disable resumes
                context.Operation.AddEvent(FlowHub.NewResumeEvent<WebHookDeliveryFlow>(webHook.Id.Value));
            }
            if (updateDiff.IsEnabled == false)
                webHook = webHook with { DisabledReason = WebHookDisabledReason.Manual };
            Validate(webHook, updateDiff);
            dbWebHook.UpdateFrom(webHook);
            if (webHook.Kind == WebHookKind.Incoming)
                await UpdateBot(webHook, updateDiff, cancellationToken).ConfigureAwait(false);
            else
                ApplyCustomHeader(dbWebHook, updateDiff);
            AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
        }
        else {
            dbWebHook.Require().RequireVersion(expectedVersion);
            dbContext.Remove(dbWebHook);
            // Pending deliveries die with the hook
            await dbContext.WebHookDeliveries
                .Where(x => x.WebHookId == dbWebHook.Id)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            var removed = dbWebHook.ToModel();
            if (removed.Kind == WebHookKind.Incoming) {
                await RetireBot(removed, cancellationToken).ConfigureAwait(false);
                AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var model = dbWebHook.ToModel();
        context.Operation.Items.KeylessSet(model);
        return new WebHookChangeResult(change.IsRemove() ? null : model, secret);
    }

    // [CommandHandler]
    public virtual async Task<string> OnRotateSecret(
        WebHooksBackend_RotateSecret command,
        CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            InvalidateHook(context);
            return default!;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbWebHook = await GetDbWebHook(dbContext, command.Id, cancellationToken).ConfigureAwait(false);
        var now = Clocks.SystemClock.Now;
        if (dbWebHook.Kind == WebHookKind.Incoming) {
            // A token has no overlap window: the old one stops working the moment the new one is minted
            var token = WebHookTokens.New();
            AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
            dbWebHook.TokenHash = WebHookTokens.Hash(token);
            AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
            dbWebHook.ModifiedAt = now;
            dbWebHook.Version = VersionGenerator.NextVersion(dbWebHook.Version);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            context.Operation.Items.KeylessSet(dbWebHook.ToModel());
            return token;
        }

        var secret = StandardWebhookSigner.NewSecret();
        dbWebHook.PrevSecretProtected = dbWebHook.SecretProtected;
        dbWebHook.PrevSecretExpiresAt = now + Constants.WebHooks.SecretOverlap;
        dbWebHook.SecretProtected = Secrets.Protect(secret);
        dbWebHook.ModifiedAt = now;
        dbWebHook.Version = VersionGenerator.NextVersion(dbWebHook.Version);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.Items.KeylessSet(dbWebHook.ToModel());
        return secret;
    }

    // [CommandHandler]
    public virtual async Task OnEnqueue(WebHooksBackend_Enqueue command, CancellationToken cancellationToken)
    {
        var (id, _, deliveryId, eventType, payload) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = ListDeliveries(id, Constants.WebHooks.DeliveryListLimit, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // A redelivered event lands here twice with the same delivery id; the second one is a no-op.
        // A failed insert would abort the operation's transaction, so the check comes first
        // rather than catching the unique violation.
        var isDuplicate = await dbContext.WebHookDeliveries
            .AnyAsync(x => x.Id == deliveryId, cancellationToken)
            .ConfigureAwait(false);
        if (isDuplicate)
            return;

        var now = Clocks.SystemClock.Now;
        var pendingDeliveries = dbContext.WebHookDeliveries
            .Where(x => x.WebHookId == id.Value && x.Status == WebHookDeliveryStatus.Pending);
        var pendingCount = await pendingDeliveries.CountAsync(cancellationToken).ConfigureAwait(false);
        if (pendingCount >= Constants.WebHooks.MaxPendingDeliveries) {
            var oldest = await pendingDeliveries
                .OrderBy(x => x.Seq)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);
            oldest.Status = WebHookDeliveryStatus.Abandoned;
            oldest.LastError = "queue overflow";
            oldest.CompletedAt = now;
        }
        dbContext.Add(new DbWebHookDelivery {
            Id = deliveryId,
            WebHookId = id.Value,
            Seq = VersionGenerator.NextVersion(),
            EventType = eventType,
            Payload = payload,
            Status = WebHookDeliveryStatus.Pending,
            CreatedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // An operation event, not a direct Schedule: the flow must not wake up before the row is committed
        context.Operation.AddEvent(FlowHub.NewResumeEvent<WebHookDeliveryFlow>(id.Value));
    }

    // [CommandHandler]
    public virtual async Task OnRecordDelivery(
        WebHooksBackend_RecordDelivery command,
        CancellationToken cancellationToken)
    {
        var (id, _, deliveryId, status, statusCode, error, latencyMs, nextAttemptAt) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            InvalidateHook(context);
            _ = ListDeliveries(id, Constants.WebHooks.DeliveryListLimit, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var dbDelivery = await dbContext.WebHookDeliveries
            .FirstOrDefaultAsync(x => x.Id == deliveryId && x.WebHookId == id.Value, cancellationToken)
            .ConfigureAwait(false);
        dbDelivery = dbDelivery.Require();
        error = CleanError(error);
        var isTerminal = status != WebHookDeliveryStatus.Pending;
        dbDelivery.Status = status;
        dbDelivery.Attempts++;
        dbDelivery.LastStatusCode = statusCode;
        dbDelivery.LastError = error;
        dbDelivery.LastLatencyMs = latencyMs;
        dbDelivery.NextAttemptAt = nextAttemptAt?.ToDateTime();
        dbDelivery.CompletedAt = isTerminal ? now : null;

        // Delivery stats don't bump the hook's Version: that would make a user's concurrent
        // edit fail its ExpectedVersion check every time a delivery lands.
        var dbWebHook = await GetDbWebHook(dbContext, id, cancellationToken).ConfigureAwait(false);
        var isSuccess = status == WebHookDeliveryStatus.Succeeded;
        dbWebHook.LastActivityAt = now;
        dbWebHook.LastStatusCode = statusCode;
        dbWebHook.LastError = error;
        dbWebHook.ConsecutiveFailures = isSuccess ? 0 : dbWebHook.ConsecutiveFailures + 1;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.Items.KeylessSet(dbWebHook.ToModel());
        AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
    }

    // [CommandHandler]
    public virtual async Task OnRecordPost(WebHooksBackend_RecordPost command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            InvalidateHook(context);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbWebHook = await GetDbWebHook(dbContext, command.Id, cancellationToken).ConfigureAwait(false);
        dbWebHook.LastActivityAt = Clocks.SystemClock.Now;
        dbWebHook.ConsecutiveFailures = 0;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.Items.KeylessSet(dbWebHook.ToModel());
        AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
    }

    // [CommandHandler]
    public virtual async Task OnDisable(WebHooksBackend_Disable command, CancellationToken cancellationToken)
    {
        var (id, _, reason, error) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            InvalidateHook(context);
            _ = HasUserScopedHooks(default);
            _ = ListDeliveries(id, Constants.WebHooks.DeliveryListLimit, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var now = Clocks.SystemClock.Now;
        var dbWebHook = await GetDbWebHook(dbContext, id, cancellationToken).ConfigureAwait(false);
        dbWebHook.IsEnabled = false;
        dbWebHook.DisabledReason = reason;
        dbWebHook.LastError = CleanError(error);
        dbWebHook.ModifiedAt = now;
        dbWebHook.Version = VersionGenerator.NextVersion(dbWebHook.Version);
        var nowUtc = now.ToDateTime();
        await dbContext.WebHookDeliveries
            .Where(x => x.WebHookId == id.Value && x.Status == WebHookDeliveryStatus.Pending)
            .ExecuteUpdateAsync(x => x
                    .SetProperty(d => d.Status, WebHookDeliveryStatus.Abandoned)
                    .SetProperty(d => d.LastError, "web hook disabled")
                    .SetProperty(d => d.CompletedAt, nowUtc),
                cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.Items.KeylessSet(dbWebHook.ToModel());
        AddInvalidatedTokenHash(context, dbWebHook.TokenHash);
    }

    // [CommandHandler]
    public virtual async Task OnRedeliver(WebHooksBackend_Redeliver command, CancellationToken cancellationToken)
    {
        var (id, _, deliveryId) = command;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = ListDeliveries(id, Constants.WebHooks.DeliveryListLimit, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbDelivery = await dbContext.WebHookDeliveries
            .FirstOrDefaultAsync(x => x.Id == deliveryId && x.WebHookId == id.Value, cancellationToken)
            .ConfigureAwait(false);
        dbDelivery = dbDelivery.Require();
        if (dbDelivery.Status == WebHookDeliveryStatus.Pending)
            throw StandardError.Constraint("Only a completed delivery can be redelivered.");

        var cloneId = $"{deliveryId}:r{dbDelivery.Attempts}";
        var isRedelivered = await dbContext.WebHookDeliveries
            .AnyAsync(x => x.Id == cloneId, cancellationToken)
            .ConfigureAwait(false);
        if (isRedelivered)
            return;

        dbContext.Add(new DbWebHookDelivery {
            Id = cloneId,
            WebHookId = dbDelivery.WebHookId,
            Seq = VersionGenerator.NextVersion(),
            EventType = dbDelivery.EventType,
            Payload = dbDelivery.Payload,
            Status = WebHookDeliveryStatus.Pending,
            CreatedAt = Clocks.SystemClock.Now,
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        context.Operation.AddEvent(FlowHub.NewResumeEvent<WebHookDeliveryFlow>(id.Value));
    }

    // [CommandHandler]
    public virtual async Task<WebHookTestResult> OnTest(
        WebHooksBackend_Test command,
        CancellationToken cancellationToken)
    {
        var (id, _, sentBy) = command;
        if (Invalidation.IsActive)
            return default!;

        var webHook = await Get(id, cancellationToken).Require().ConfigureAwait(false);
        if (webHook.Kind == WebHookKind.Incoming) {
            var startedAt = CpuTimestamp.Now;
            var posted = await WebHookInbox.PostTest(webHook, sentBy, cancellationToken).ConfigureAwait(false);
            return new WebHookTestResult(posted.IsOk, posted.StatusCode, posted.Error, LatencyMs(startedAt));
        }

        return await Deliverer.SendPing(webHook, sentBy, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private void InvalidateHook(CommandContext context)
    {
        // Create mints the id inside the handler, so every command reads the hook back from the operation
        if (context.Operation.Items.KeylessGet<WebHook>() is not { } webHook)
            return;

        _ = Get(webHook.Id, default);
        _ = ListByScope(webHook.Scope, webHook.ScopeId, default);
        if (webHook.CreatedBy is { } createdBy)
            _ = ListByCreator(createdBy, default);
        foreach (var tokenHash in context.Operation.Items.KeylessGet<InvalidatedTokenHashes>()?.Hashes ?? [])
            _ = GetByTokenHash(tokenHash, default);
    }

    // Any write to a hook row must invalidate its token lookup, not just the ones changing the token
    private static void AddInvalidatedTokenHash(CommandContext context, string? tokenHash)
    {
        if (tokenHash.IsNullOrEmpty())
            return;

        var hashes = context.Operation.Items.KeylessGet<InvalidatedTokenHashes>()?.Hashes ?? [];
        if (hashes.Contains(tokenHash))
            return;

        context.Operation.Items.KeylessSet(new InvalidatedTokenHashes([..hashes, tokenHash]));
    }

    private async Task EnsureBot(WebHook webHook, ChatId chatId, WebHookDiff diff, CancellationToken cancellationToken)
    {
        var botUserId = webHook.Id.ToBotUserId();
        var displayName = diff.DisplayName?.Trim().NullIfEmpty() ?? webHook.Name;
        var info = new InternalUserInfo(botUserId, displayName, AvatarName: displayName) {
            AvatarMediaId = diff.AvatarMediaId.IsSome(out var mediaId) ? mediaId : null,
            IsBot = true,
        };
        await InternalAccounts.Create(Services, info, cancellationToken).ConfigureAwait(false);
        // HasLeft = false: an earlier attempt's bot may have been excluded as an orphan meanwhile
        var upsert = new AuthorsBackend_Upsert(
            chatId, null, botUserId, null, new AuthorDiff { HasLeft = false }, DoNotNotify: true);
        await Commander.Call(upsert, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateBot(WebHook webHook, WebHookDiff diff, CancellationToken cancellationToken)
    {
        if (diff.DisplayName is null && !diff.AvatarMediaId.HasValue)
            return;

        var account = await AccountsBackend.Get(webHook.Id.ToBotUserId(), cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var avatar = account.Avatar;
        var change = new AvatarsBackend_Change(avatar.Id, avatar.Version, Change.Update(new AvatarDiff {
            Name = diff.DisplayName?.Trim().NullIfEmpty(),
            MediaId = diff.AvatarMediaId,
        }));
        await Commander.Call(change, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task RetireBot(WebHook webHook, CancellationToken cancellationToken)
    {
        var botUserId = webHook.Id.ToBotUserId();
        var chatId = ChatId.Parse(webHook.ScopeId);
        var author = await AuthorsBackend
            .GetByUserId(chatId, botUserId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is { HasLeft: false }) {
            var leave = new AuthorsBackend_Upsert(chatId, author.Id, null, author.Version,
                new AuthorDiff { HasLeft = true }, DoNotNotify: true);
            await Commander.Call(leave, true, cancellationToken).ConfigureAwait(false);
        }
        var account = await AccountsBackend.Get(botUserId, cancellationToken).ConfigureAwait(false);
        if (account is { Status: not AccountStatus.Suspended }) {
            var suspend = new AccountsBackend_Update(
                account with { Status = AccountStatus.Suspended }, account.Version);
            await Commander.Call(suspend, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<DbWebHook> GetDbWebHook(
        ChatDbContext dbContext,
        WebHookId id,
        CancellationToken cancellationToken)
    {
        var dbWebHook = await dbContext.WebHooks
            .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbWebHook.Require();
    }

    private void Validate(WebHook webHook, WebHookDiff diff)
    {
        if (webHook.Name.IsNullOrWhiteSpace())
            throw StandardError.Constraint("Web hook name is required.");
        if (webHook.Name.Length > Constants.WebHooks.MaxNameLength)
            throw StandardError.Constraint(
                $"Web hook name can't be longer than {Constants.WebHooks.MaxNameLength} characters.");
        // An incoming hook is addressed by its token: it carries none of the delivery settings
        if (webHook.Kind == WebHookKind.Incoming) {
            if (!webHook.Url.IsNullOrEmpty())
                throw StandardError.Constraint("An incoming web hook can't have a URL.");
            if (webHook.Events != WebHookEvents.None)
                throw StandardError.Constraint("An incoming web hook can't subscribe to events.");
            if (webHook.ChatIds.Count != 0)
                throw StandardError.Constraint("An incoming web hook posts to its own chat only.");
            if (webHook.SubscribeNotifications)
                throw StandardError.Constraint("An incoming web hook can't subscribe to notifications.");
            if (!webHook.CustomHeaderName.IsNullOrEmpty() || !diff.CustomHeaderValue.IsNullOrEmpty())
                throw StandardError.Constraint("An incoming web hook can't have a custom header.");

            return;
        }

        if (!IsSchemeAllowed(webHook.Url, HostInfo, out var uri))
            throw StandardError.Constraint("Web hook URL must be an absolute https:// URL.");
        if (!EgressGuard.IsAllowedUri(uri))
            throw StandardError.Constraint(
                "Web hook URL must use a public host name, not an IP address or an internal domain.");
        if (webHook.CustomHeaderName is { } headerName) {
            if (!headerName.All(IsHeaderTokenChar))
                throw StandardError.Constraint("Header name can contain only letters, digits and !#$%&'*+-.^_`|~.");
            if (ReservedHeaderNames.Contains(headerName))
                throw StandardError.Constraint("This header name is reserved.");
        }
        if (diff.CustomHeaderValue is { } headerValue && headerValue.Any(char.IsControl))
            throw StandardError.Constraint("Header value can't contain line breaks or control characters.");
        if (webHook.Events == WebHookEvents.None)
            throw StandardError.Constraint("Select at least one event.");
        if (webHook.Scope == WebHookScope.User && !webHook.SubscribeNotifications && webHook.ChatIds.Count == 0)
            throw StandardError.Constraint(
                "A user web hook must subscribe to notifications or list at least one chat.");
    }

    // Checked on save and again at delivery time (WebHookDeliverer), so it's static and shared
    internal static bool IsAllowedUrl(string url, HostInfo hostInfo, EgressGuard egressGuard)
        => IsSchemeAllowed(url, hostInfo, out var uri) && egressGuard.IsAllowedUri(uri);

    private static bool IsSchemeAllowed(string url, HostInfo hostInfo, [NotNullWhen(true)] out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            return false;
        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        // Plain http is fine only for a loopback receiver on a dev box or in tests
        return uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && (hostInfo.IsDevelopmentInstance || hostInfo.IsTested);
    }

    private static bool IsHeaderTokenChar(char c)
        => char.IsAsciiLetterOrDigit(c) || HeaderTokenSymbols.Contains(c);

    // Receiver-controlled text lands in the UI and the log, so it's bounded and printable
    private static string? CleanError(string? error)
    {
        if (error.IsNullOrEmpty())
            return error;

        var cleaned = new string(error.Where(c => !char.IsControl(c)).ToArray());
        var maxLength = Constants.WebHooks.MaxErrorLength;
        if (cleaned.Length <= maxLength)
            return cleaned;

        // A lone high surrogate is not valid UTF-8, so Npgsql would refuse to store it
        if (char.IsHighSurrogate(cleaned[maxLength - 1]))
            maxLength--;
        return cleaned[..maxLength];
    }

    private void ApplyCustomHeader(DbWebHook dbWebHook, WebHookDiff diff)
    {
        if (diff.CustomHeaderValue is { } headerValue)
            dbWebHook.CustomHeaderValueProtected = headerValue.IsNullOrEmpty() ? null : Secrets.Protect(headerValue);
        if (dbWebHook.CustomHeaderName.IsNullOrEmpty())
            dbWebHook.CustomHeaderValueProtected = null;
    }

    private static int LatencyMs(CpuTimestamp startedAt)
        => (int)Math.Min(startedAt.Elapsed.TotalMilliseconds, int.MaxValue);

    // Nested types

    // Read back during the invalidation phase, possibly on another node: Operation.Items round-trips
    // through _Operations.ItemsJson, so the serialization attributes are what makes it survive
    [DataContract, MessagePackObject(AllowPrivate = true)]
    internal sealed partial record InvalidatedTokenHashes(
        [property: DataMember(Order = 0), Key(0)] string[] Hashes);
}
