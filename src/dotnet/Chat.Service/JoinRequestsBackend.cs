using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class JoinRequestsBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IJoinRequestsBackend
{
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();

    // [ComputeMethod]
    public virtual async Task<JoinRequest?> Get(ChatId chatId, JoinRequestId id, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRequest = await dbContext.JoinRequests
            .FirstOrDefaultAsync(r => r.Id == id.Value && r.ChatId == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        return dbRequest is null ? null : ToFreshModel(dbRequest);
    }

    // [ComputeMethod]
    public virtual async Task<JoinRequest?> GetLatest(
        ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRequest = await dbContext.JoinRequests
            .Where(r => r.ChatId == chatId.Value && r.UserId == userId.Value)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbRequest is null)
            return null;

        var request = ToFreshModel(dbRequest);
        var now = Clocks.SystemClock.Now;
        var changesAt = request.IsOpen ? request.ExpiresAt : request.GetRejectedUntil(now);
        if (changesAt is { } moment)
            Computed.GetCurrent().Invalidate(moment - now);
        return request;
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<JoinRequest>> ListPending(ChatId chatId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRequests = await dbContext.JoinRequests
            .Where(r => r.ChatId == chatId.Value && r.Status == JoinRequestStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var requests = dbRequests
            .Select(ToFreshModel)
            .Where(r => r.Status == JoinRequestStatus.Pending)
            .ToApiArray();
        if (requests.Count > 0) {
            var nextExpiresAt = requests.Min(r => r.ExpiresAt);
            Computed.GetCurrent().Invalidate(nextExpiresAt - Clocks.SystemClock.Now);
        }
        return requests;
    }

    // Not a [ComputeMethod]
    public virtual async Task<ApiArray<JoinRequest>> ListExpired(
        ThisNodeRef nodeRef, int limit, CancellationToken cancellationToken)
    {
        var now = Clocks.SystemClock.Now.ToDateTime();
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRequests = await dbContext.JoinRequests
            .Where(r => r.Status == JoinRequestStatus.Pending || r.Status == JoinRequestStatus.AwaitingPlace)
            .Where(r => r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbRequests.Select(r => r.ToModel()).ToApiArray();
    }

    // [CommandHandler]
    public virtual async Task<JoinRequest> OnCreate(
        JoinRequestsBackend_Create command, CancellationToken cancellationToken)
    {
        var (chatId, userId, parentId) = command;
        var context = CommandContext.GetCurrent();
        var now = Clocks.SystemClock.Now;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        await dbContext.JoinRequests.Lock(chatId, userId, cancellationToken).ConfigureAwait(false);
        var dbOpenRequest = await dbContext.JoinRequests
            .Where(r => r.ChatId == chatId.Value && r.UserId == userId.Value)
            .Where(r => r.Status == JoinRequestStatus.Pending || r.Status == JoinRequestStatus.AwaitingPlace)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dbOpenRequest is not null) {
            var openRequest = dbOpenRequest.ToModel();
            if (openRequest.ExpiresAt > now)
                return openRequest;

            // Expired but not swept yet: close it here, the unique index allows one open request
            var expiredRequest = Close(openRequest, JoinRequestStatus.Expired, null, now);
            dbOpenRequest.UpdateFrom(expiredRequest);
            AddJournalRow(dbContext, expiredRequest, null, now);
            context.Operation.AddEvent(new JoinRequestChangedEvent(expiredRequest, openRequest));
        }

        var lastMembershipEvent = await dbContext.MembershipEvents
            .Where(e => e.ChatId == chatId.Value && e.UserId == userId.Value)
            .Where(e => e.Kind == MembershipEventKind.Joined || e.Kind == MembershipEventKind.Left)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var request = new JoinRequest(JoinRequestId.New(), VersionGenerator.NextVersion()) {
            ChatId = chatId,
            UserId = userId,
            Status = parentId is null ? JoinRequestStatus.Pending : JoinRequestStatus.AwaitingPlace,
            ParentId = parentId,
            InviteId = command.InviteId,
            WasExcluded = lastMembershipEvent is { Kind: MembershipEventKind.Left, Method: MembershipMethod.Excluded },
            CreatedAt = now,
            ExpiresAt = now + Constants.Chat.JoinRequestLifespan,
        };
        dbContext.JoinRequests.Add(new DbJoinRequest(request));
        AddJournalRow(dbContext, request, userId, now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvalidateRequest(request);
        context.Operation.AddEvent(new JoinRequestChangedEvent(request, null));
        return request;
    }

    // [CommandHandler]
    public virtual async Task<JoinRequest> OnChangeStatus(
        JoinRequestsBackend_ChangeStatus command, CancellationToken cancellationToken)
    {
        var (chatId, id, status, actorId) = command;
        if (status == JoinRequestStatus.AwaitingPlace)
            throw StandardError.Constraint("A join request can't go back to waiting for its Place.");

        var context = CommandContext.GetCurrent();
        var now = Clocks.SystemClock.Now;

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbRequest = await dbContext.JoinRequests.ForUpdate()
            .FirstOrDefaultAsync(r => r.Id == id.Value && r.ChatId == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        var oldRequest = dbRequest.Require().ToModel();
        if (oldRequest.Status == status)
            return oldRequest;
        if (!oldRequest.IsOpen)
            throw StandardError.Constraint("This join request is already closed.");

        var request = status == JoinRequestStatus.Pending
            // Shown to the chat's moderators only now, so its lifespan starts now
            ? oldRequest with {
                Version = VersionGenerator.NextVersion(oldRequest.Version),
                Status = status,
                ExpiresAt = now + Constants.Chat.JoinRequestLifespan,
            }
            : Close(oldRequest, status, actorId, now);
        dbRequest!.UpdateFrom(request);
        if (status != JoinRequestStatus.Pending)
            AddJournalRow(dbContext, request, actorId, now);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        InvalidateRequest(request);
        context.Operation.AddEvent(new JoinRequestChangedEvent(request, oldRequest));
        return request;
    }

    // [EventHandler]
    public virtual async Task OnJoinRequestChangedEvent(
        JoinRequestChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        var (request, oldRequest) = eventCommand;
        if (request.IsOpen || oldRequest is not { IsOpen: true })
            return;

        if (request.ParentId is { } parentId) {
            // Withdrawing the chat request withdraws the Place request filed along with it
            if (request.Status != JoinRequestStatus.Withdrawn || request.ChatId is not PlaceChatId placeChatId)
                return;

            var parent = await Get(placeChatId.PlaceId.RootChatId, parentId, cancellationToken).ConfigureAwait(false);
            if (parent is { IsOpen: true }) {
                var withdrawCmd = new JoinRequestsBackend_ChangeStatus(
                    parent.ChatId, parent.Id, JoinRequestStatus.Withdrawn, request.UserId);
                await Commander.Call(withdrawCmd, true, cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        var children = await ListOpenChildren(request.Id, cancellationToken).ConfigureAwait(false);
        foreach (var child in children)
            await OnParentClosed(request, child, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task OnParentClosed(JoinRequest parent, JoinRequest child, CancellationToken cancellationToken)
    {
        if (parent.Status != JoinRequestStatus.Approved) {
            var closeCmd = new JoinRequestsBackend_ChangeStatus(
                child.ChatId, child.Id, parent.Status, parent.DecidedBy);
            await Commander.Call(closeCmd, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (child.InviteId.IsEmpty) {
            var showCmd = new JoinRequestsBackend_ChangeStatus(child.ChatId, child.Id, JoinRequestStatus.Pending);
            await Commander.Call(showCmd, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The invite link pre-approved the chat, so the person goes in as soon as the Place lets them
        var change = new MembershipChange(MembershipMethod.InviteLink, child.UserId) {
            InviteId = child.InviteId,
            RequestId = child.Id,
        };
        await AuthorsBackend.Admit(child.ChatId, child.UserId, change, cancellationToken).ConfigureAwait(false);
        var approveCmd = new JoinRequestsBackend_ChangeStatus(child.ChatId, child.Id, JoinRequestStatus.Approved);
        await Commander.Call(approveCmd, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<JoinRequest>> ListOpenChildren(JoinRequestId parentId, CancellationToken cancellationToken)
    {
        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbChildren = await dbContext.JoinRequests
            .Where(r => r.ParentId == parentId.Value)
            .Where(r => r.Status == JoinRequestStatus.Pending || r.Status == JoinRequestStatus.AwaitingPlace)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return dbChildren.Select(r => r.ToModel()).ToList();
    }

    private void InvalidateRequest(JoinRequest request)
        => Invalidation.Defer(() => {
            _ = Get(request.ChatId, request.Id, default);
            _ = GetLatest(request.ChatId, request.UserId, default);
            _ = ListPending(request.ChatId, default);
        });

    private JoinRequest Close(JoinRequest request, JoinRequestStatus status, UserId? actorId, Moment now)
        => request with {
            Version = VersionGenerator.NextVersion(request.Version),
            Status = status,
            DecidedAt = now,
            DecidedBy = actorId,
        };

    private static void AddJournalRow(ChatDbContext dbContext, JoinRequest request, UserId? actorId, Moment now)
    {
        var kind = request.Status switch {
            JoinRequestStatus.Approved => MembershipEventKind.RequestApproved,
            JoinRequestStatus.Rejected => MembershipEventKind.RequestRejected,
            JoinRequestStatus.Withdrawn => MembershipEventKind.RequestWithdrawn,
            JoinRequestStatus.Expired => MembershipEventKind.RequestExpired,
            _ => MembershipEventKind.RequestSubmitted,
        };
        var change = new MembershipChange(MembershipMethod.JoinRequest, actorId) {
            InviteId = request.InviteId,
            RequestId = request.Id,
        };
        dbContext.MembershipEvents.Add(
            DbMembershipEvent.New(request.ChatId, request.UserId, null, kind, change, now));
    }

    private JoinRequest ToFreshModel(DbJoinRequest dbRequest)
    {
        // An open request past its lifespan reads as expired before the sweeper gets to it
        var request = dbRequest.ToModel();
        return request.IsOpen && request.ExpiresAt <= Clocks.SystemClock.Now
            ? request with { Status = JoinRequestStatus.Expired }
            : request;
    }
}
