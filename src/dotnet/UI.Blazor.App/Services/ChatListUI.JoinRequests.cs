namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatListUI
{
    private IJoinRequests JoinRequests => field ??= Services.GetRequiredService<IJoinRequests>();

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<JoinRequestGroup>> ListJoinRequestGroups(
        PlaceId? placeId, CancellationToken cancellationToken = default)
    {
        // The pending requests of the chats and Places the user decides on: the ones in the list, plus the Places
        var session = Session;
        var chatsService = Hub.Chats;
        var joinRequests = JoinRequests;
        PlaceId[] placeIds = placeId is not null ? [placeId] : [];
        if (placeId is null)
            placeIds = await Contacts.ListPlaceIds(session, cancellationToken).ConfigureAwait(false);
        var chatById = await ListUnordered(placeId, cancellationToken).ConfigureAwait(false);
        var chats = chatById.Values
            .Select(c => c.Chat)
            .Where(c => c.Kind is ChatKind.Group or ChatKind.Place)
            .ToList();
        var rootChats = await placeIds
            .Select(id => chatsService.Get(session, id.RootChatId, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        chats.AddRange(rootChats.SkipNullItems());

        var candidates = chats
            .Where(c => !c.IsPublic && c.Rules.CanEditMembers())
            .DistinctBy(c => c.Id)
            .ToList();
        var groups = await candidates
            .Select(async chat => {
                var requests = await joinRequests.ListPending(session, chat.Id, cancellationToken)
                    .ConfigureAwait(false);
                return requests.Count == 0 ? null : new JoinRequestGroup(chat, requests);
            })
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return groups
            .SkipNullItems()
            .OrderBy(g => g.Requests[0].CreatedAt)
            .ToList();
    }
}
