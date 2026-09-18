using ActualChat.Notifications;
using ActualChat.WebHooks;

namespace ActualChat.Chat;

public partial class WebHooksBackend
{
    // [EventHandler]
    public virtual async Task OnChatEntryChangedEvent(
        ChatEntryChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (entry, author, changeKind, oldEntry) = eventCommand;
        if (entry.IsSystemEntry)
            return;

        // Typed text arrives complete in Create; a voice message is created empty + streaming and its
        // final text lands in the streaming -> finalized Update, so that transition is its "posted".
        // Removal usually comes as Remove, but a thread start is removed (and any entry restored)
        // through an Update flipping IsRemoved.
        var e = changeKind switch {
            ChangeKind.Create when !entry.IsContentStreaming => WebHookEvents.MessagePosted,
            ChangeKind.Update when oldEntry is { IsContentStreaming: true } && !entry.IsContentStreaming
                => WebHookEvents.MessagePosted,
            ChangeKind.Update when oldEntry is { IsRemoved: false } && entry.IsRemoved
                => WebHookEvents.MessageRemoved,
            ChangeKind.Update when oldEntry is { IsRemoved: true } && !entry.IsRemoved
                => WebHookEvents.MessagePosted,
            ChangeKind.Update when oldEntry is { IsContentStreaming: false }
                && !entry.IsContentStreaming
                && IsEdited(oldEntry, entry)
                => WebHookEvents.MessageEdited,
            ChangeKind.Remove => WebHookEvents.MessageRemoved,
            _ => WebHookEvents.None,
        };
        if (e == WebHookEvents.None)
            return;

        var previous = e == WebHookEvents.MessageEdited ? oldEntry : null;
        var hooks = await HooksForChat(entry.ChatId, cancellationToken).ConfigureAwait(false);
        await Enqueue(hooks, e, $"{entry.LocalId}:{entry.Version}",
                hook => Payloads.Message(hook, e, entry, previous, author, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnReactionChangedEvent(
        ReactionChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (reaction, entry, _, reactionAuthor, changeKind) = eventCommand;
        var e = changeKind == ChangeKind.Remove ? WebHookEvents.ReactionRemoved : WebHookEvents.ReactionAdded;
        var hooks = await HooksForChat(entry.ChatId, cancellationToken).ConfigureAwait(false);
        await Enqueue(hooks, e, reaction.Id.Value,
                hook => Payloads.Reaction(hook, e, reaction, entry, reactionAuthor, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnAuthorUpsertedEvent(
        AuthorUpsertedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (author, oldAuthor) = eventCommand;
        if (!IsMemberEventAuthor(author))
            return;

        var e = oldAuthor is null || oldAuthor.HasLeft
            ? author.HasLeft ? WebHookEvents.None : WebHookEvents.MemberJoined
            : author.HasLeft ? WebHookEvents.MemberLeft : WebHookEvents.None;
        if (e == WebHookEvents.None)
            return;

        var hooks = await HooksForChat(author.ChatId, cancellationToken).ConfigureAwait(false);
        await EnqueueMember(hooks, e, author, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnAuthorsRemovedEvent(
        AuthorsRemovedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        foreach (var chatAuthors in eventCommand.Authors.Where(IsMemberEventAuthor).GroupBy(x => x.ChatId)) {
            var hooks = await HooksForChat(chatAuthors.Key, cancellationToken).ConfigureAwait(false);
            foreach (var author in chatAuthors)
                await EnqueueMember(hooks, WebHookEvents.MemberLeft, author, cancellationToken).ConfigureAwait(false);
        }
    }

    // [EventHandler]
    public virtual async Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (chat, oldChat, changeKind) = eventCommand;
        WebHookEvents e;
        List<WebHook> hooks;
        switch (changeKind) {
        case ChangeKind.Update:
            if (!IsChatUpdated(chat, oldChat))
                return;

            e = WebHookEvents.ChatUpdated;
            hooks = await HooksForChat(chat.Id, cancellationToken).ConfigureAwait(false);
            break;
        case ChangeKind.Create:
        case ChangeKind.Remove:
            if (chat.Id is not PlaceChatId placeChatId)
                return;

            e = changeKind == ChangeKind.Create ? WebHookEvents.ChatCreated : WebHookEvents.ChatArchived;
            hooks = await HooksForPlace(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            break;
        default:
            return;
        }
        await Enqueue(hooks, e, chat.Version.ToString(),
                hook => Payloads.ChatChanged(hook, e, chat, oldChat, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnPlaceChangedEvent(PlaceChangedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (place, oldPlace, changeKind) = eventCommand;
        if (changeKind != ChangeKind.Update)
            return;

        var e = WebHookEvents.PlaceUpdated;
        var hooks = await HooksForPlace(place.Id, cancellationToken).ConfigureAwait(false);
        await Enqueue(hooks, e, place.Version.ToString(),
                hook => Payloads.PlaceChanged(hook, e, place, oldPlace, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnPlaceMembershipChangedEvent(
        PlaceMembershipChangedEvent eventCommand,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (userId, placeId, hasLeft) = eventCommand;
        var author = await AuthorsBackend
            .GetByUserId(placeId.RootChatId, userId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author is null)
            return;

        var e = hasLeft ? WebHookEvents.PlaceMemberLeft : WebHookEvents.PlaceMemberJoined;
        var hooks = await HooksForPlace(placeId, cancellationToken).ConfigureAwait(false);
        await EnqueueMember(hooks, e, author, cancellationToken).ConfigureAwait(false);
    }

    // [EventHandler]
    public virtual async Task OnUserNotifiedEvent(UserNotifiedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var n = eventCommand.Notification;
        if (n is not ChatNotification cn)
            return;

        var hooks = (await ListActiveForUser(n.UserId, cancellationToken).ConfigureAwait(false))
            .Where(h => h.SubscribeNotifications)
            .ToList();
        if (hooks.Count == 0)
            return;

        var entryId = cn is ChatEntryNotification en ? en.EntryId : (ChatEntryId?)null;
        var entry = await ChatsBackend.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
        var author = entry is null
            ? null
            : await AuthorsBackend.Get(entry.ChatId, entry.AuthorId, RequestedAuthorKind.Full, cancellationToken)
                .ConfigureAwait(false);
        await Enqueue(hooks, WebHookEvents.Notification, n.Id.Value,
                hook => Payloads.Notification(hook, n, entry, author, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private Task EnqueueMember(
        List<WebHook> hooks,
        WebHookEvents e,
        AuthorFull author,
        CancellationToken cancellationToken)
        => Enqueue(hooks, e, $"{author.Id.Value}:{author.Version}",
            hook => Payloads.Member(hook, e, author, cancellationToken),
            cancellationToken);

    private async Task Enqueue(
        IEnumerable<WebHook> hooks,
        WebHookEvents e,
        string eventKey,
        Func<WebHook, Task<string>> payload,
        CancellationToken cancellationToken)
    {
        var eventType = e.ToEventType();
        foreach (var hook in hooks) {
            if (!hook.Events.HasFlag(e))
                continue;

            var json = await payload(hook).ConfigureAwait(false);
            var command = new WebHooksBackend_Enqueue(
                hook.Id, hook.ScopeId, WebHookPayloads.DeliveryId(hook.Id, eventType, eventKey), eventType, json);
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<WebHook>> HooksForChat(ChatId chatId, CancellationToken cancellationToken)
    {
        // Chat + place hooks for the chat, plus personal "selected chats" hooks of members who can still read it
        var hooks = (await ListActiveForChat(chatId, cancellationToken).ConfigureAwait(false)).ToList();
        var userIds = await AuthorsBackend.ListUserIds(chatId, cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var personal = await ListActiveForUser(userId, cancellationToken).ConfigureAwait(false);
            foreach (var hook in personal.Where(h => h.ChatIds.Contains(chatId))) {
                var rules = await ChatsBackend.GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
                if (rules.CanRead())
                    hooks.Add(hook);
            }
        }
        return hooks.DistinctBy(h => h.Id).ToList();
    }

    private async Task<List<WebHook>> HooksForPlace(PlaceId placeId, CancellationToken cancellationToken)
    {
        var hooks = await ListByScope(WebHookScope.Place, placeId.Value, cancellationToken).ConfigureAwait(false);
        return hooks.Where(x => x.IsActiveOutgoing).ToList();
    }

    private static bool IsMemberEventAuthor(AuthorFull author)
        // The place root chat is not a user-facing chat: its members are the place's, reported by the place events
        => !Bots.IsBot(author.Id) && author.ChatId is not PlaceChatId { IsRoot: true };

    private static bool IsEdited(ChatEntry oldEntry, ChatEntry entry)
        => oldEntry.Content != entry.Content
            || !oldEntry.Attachments.Select(x => x.MediaId).SequenceEqual(entry.Attachments.Select(x => x.MediaId));

    private static bool IsChatUpdated(Chat chat, Chat? oldChat)
        => oldChat is null
            || chat.Title != oldChat.Title
            || chat.Description != oldChat.Description
            || chat.MediaId != oldChat.MediaId;
}
