using ActualChat.Media;

namespace ActualChat.Chat;

public class JoinRequests(IServiceProvider services) : IJoinRequests
{
    private IServiceProvider Services { get; } = services;
    private IJoinRequestsBackend Backend => field ??= Services.GetRequiredService<IJoinRequestsBackend>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IPlacesBackend PlacesBackend => field ??= Services.GetRequiredService<IPlacesBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ICommander Commander => field ??= Services.Commander();

    // [ComputeMethod]
    public virtual async Task<JoinTarget?> GetTarget(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var userId = account.IsGuestOrNull() ? null : account.Id;
        var view = await GetView(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (view is null)
            return null;

        var state = JoinTargetState.Member;
        Moment? rejectedUntil = null;
        if (!view.IsMember) {
            if (view.CanJoinAtOnce)
                state = JoinTargetState.CanJoin;
            else if (userId is null)
                state = JoinTargetState.SignInToRequest;
            else {
                var latest = await GetLatestRequest(view, userId, cancellationToken).ConfigureAwait(false);
                rejectedUntil = latest?.GetRejectedUntil(Clocks.SystemClock.Now);
                state = latest is { IsOpen: true } ? JoinTargetState.RequestPending
                    : rejectedUntil is not null ? JoinTargetState.Rejected
                    : JoinTargetState.CanRequest;
            }
        }

        var chat = view.Chat;
        var picture = await MediaBackend.Get(chat.MediaId, cancellationToken).ConfigureAwait(false);
        return new JoinTarget(chatId, state) {
            Title = chat.Title,
            Picture = picture,
            PlaceTitle = view.Place is { } place && chatId is PlaceChatId { IsRoot: false } ? place.Title : "",
            AlsoRequestsPlace = view.MustRequestPlace && !chat.IsPublic,
            RejectedUntil = rejectedUntil,
        };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<JoinRequest>> ListPending(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null || !chat.Rules.CanEditMembers())
            return ApiArray<JoinRequest>.Empty;

        return await Backend.ListPending(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<JoinRequest> OnSubmit(JoinRequests_Submit command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var chatId = command.ChatId;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);
        var userId = account.Id;

        var view = await GetView(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (view is null)
            throw StandardError.NotFound<Chat>();
        if (view.IsMember)
            throw StandardError.Constraint("You are already a member.");
        if (view.CanJoinAtOnce)
            throw StandardError.Constraint("This chat can be joined without a request.");

        var latest = await GetLatestRequest(view, userId, cancellationToken).ConfigureAwait(false);
        if (latest?.GetRejectedUntil(Clocks.SystemClock.Now) is not null)
            throw StandardError.Constraint("Your request was declined recently. Please try again later.");

        if (view.Place is { IsPublic: true } place && !view.IsPlaceMember) {
            var joinPlaceCmd = new Places_Join { Session = session, PlaceId = place.Id };
            await Commander.Call(joinPlaceCmd, cancellationToken).ConfigureAwait(false);
        }
        if (!view.MustRequestPlace) {
            var createCmd = new JoinRequestsBackend_Create(chatId, userId);
            return await Commander.Call(createCmd, cancellationToken).ConfigureAwait(false);
        }

        var rootChatId = ((PlaceChatId)chatId).PlaceId.RootChatId;
        var createPlaceRequestCmd = new JoinRequestsBackend_Create(rootChatId, userId);
        var placeRequest = await Commander.Call(createPlaceRequestCmd, cancellationToken).ConfigureAwait(false);
        // A public chat of the Place comes with the Place membership
        if (view.Chat.IsPublic)
            return placeRequest;

        var createChatRequestCmd = new JoinRequestsBackend_Create(chatId, userId, placeRequest.Id);
        return await Commander.Call(createChatRequestCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnWithdraw(JoinRequests_Withdraw command, CancellationToken cancellationToken)
    {
        var session = command.Session;
        var chatId = command.ChatId;
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustNotBeGuest);

        var request = await Backend.GetLatest(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        if (request is not { IsOpen: true } && chatId is PlaceChatId { IsRoot: false } placeChatId) {
            var rootChatId = placeChatId.PlaceId.RootChatId;
            request = await Backend.GetLatest(rootChatId, account.Id, cancellationToken).ConfigureAwait(false);
        }
        if (request is not { IsOpen: true })
            return;

        var withdrawCmd = new JoinRequestsBackend_ChangeStatus(
            request.ChatId, request.Id, JoinRequestStatus.Withdrawn, account.Id);
        await Commander.Call(withdrawCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnApprove(JoinRequests_Approve command, CancellationToken cancellationToken)
    {
        var (request, actorId) = await GetRequestToDecide(
            command.Session, command.ChatId, command.RequestId, cancellationToken).ConfigureAwait(false);

        var change = new MembershipChange(MembershipMethod.JoinRequest, actorId) {
            InviteId = request.InviteId,
            RequestId = request.Id,
        };
        await AuthorsBackend.Admit(request.ChatId, request.UserId, change, cancellationToken).ConfigureAwait(false);
        var approveCmd = new JoinRequestsBackend_ChangeStatus(
            request.ChatId, request.Id, JoinRequestStatus.Approved, actorId);
        await Commander.Call(approveCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnReject(JoinRequests_Reject command, CancellationToken cancellationToken)
    {
        var (request, actorId) = await GetRequestToDecide(
            command.Session, command.ChatId, command.RequestId, cancellationToken).ConfigureAwait(false);

        var rejectCmd = new JoinRequestsBackend_ChangeStatus(
            request.ChatId, request.Id, JoinRequestStatus.Rejected, actorId);
        await Commander.Call(rejectCmd, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<TargetView?> GetView(ChatId chatId, UserId? userId, CancellationToken cancellationToken)
    {
        if (chatId.Kind is not (ChatKind.Group or ChatKind.Place))
            return null;

        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        // System group chats (Notes, Announcements and the like) are never joined by a link
        if (chat is null || chat.Kind == ChatKind.Group && !chat.SystemTag.IsEmpty)
            return null;

        Place? place = null;
        var isPlaceMember = false;
        if (chatId is PlaceChatId placeChatId) {
            place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return null;

            isPlaceMember = await IsMember(placeChatId.PlaceId.RootChatId, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var isInnerChat = chatId is PlaceChatId { IsRoot: false };
        // A public chat of a Place has no members of its own: the Place members are its members
        var isMember = isPlaceMember;
        if (!isInnerChat || !chat.IsPublic)
            isMember = await IsMember(chatId, userId, cancellationToken).ConfigureAwait(false);
        var isPlaceReachable = !isInnerChat || place!.IsPublic || isPlaceMember;
        var view = new TargetView(chat, place, isMember, isPlaceMember) {
            CanJoinAtOnce = chat.IsPublic && isPlaceReachable,
            MustRequestPlace = !isPlaceReachable,
        };
        if (isMember || view.CanJoinAtOnce)
            return view;

        var access = chat.IsPublic ? JoinRequestAccess.Anyone : chat.JoinRequestAccess;
        if (!isPlaceReachable)
            access = access.Min(place!.JoinRequestAccess);
        return access.Allows(userId is not null, isPlaceReachable) ? view : null;
    }

    private async Task<bool> IsMember(ChatId chatId, UserId? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
            return false;

        var author = await AuthorsBackend
            .GetByUserId(chatId, userId, RequestedAuthorKind.Default, cancellationToken)
            .ConfigureAwait(false);
        return author is { HasLeft: false };
    }

    private async Task<JoinRequest?> GetLatestRequest(
        TargetView view, UserId userId, CancellationToken cancellationToken)
    {
        var chatId = view.Chat.Id;
        JoinRequest? latest = null;
        // A public chat in a private Place takes a Place request only
        if (!view.Chat.IsPublic || !view.MustRequestPlace)
            latest = await Backend.GetLatest(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (!view.MustRequestPlace || latest is { IsOpen: true })
            return latest;

        // The Place request decides what the person sees until the Place lets them in
        var rootChatId = ((PlaceChatId)chatId).PlaceId.RootChatId;
        var placeRequest = await Backend.GetLatest(rootChatId, userId, cancellationToken).ConfigureAwait(false);
        return placeRequest ?? latest;
    }

    private async Task<(JoinRequest Request, UserId ActorId)> GetRequestToDecide(
        Session session, ChatId chatId, JoinRequestId requestId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Require(ChatPermissions.EditMembers);

        var request = await Backend.Get(chatId, requestId, cancellationToken).Require().ConfigureAwait(false);
        if (request.Status != JoinRequestStatus.Pending)
            throw StandardError.Constraint("This join request is already closed.");

        return (request, chat.Rules.Account.Require().Id);
    }

    // Nested types

    /// <summary>
    /// A chat as someone outside it sees it. <see cref="MustRequestPlace"/> means it is in a private
    /// Place the person isn't in, so they ask for the Place first.
    /// </summary>
    private sealed record TargetView(Chat Chat, Place? Place, bool IsMember, bool IsPlaceMember)
    {
        public bool CanJoinAtOnce { get; init; }
        public bool MustRequestPlace { get; init; }
    }
}
