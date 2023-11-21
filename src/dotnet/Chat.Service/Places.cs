namespace ActualChat.Chat;

public class Places(IServiceProvider services) : IPlaces
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var chatId = placeId.ToRootChatId();
        var placeRootChat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (placeRootChat == null)
            return null;

        if (!placeRootChat.Rules.CanRead())
            return null;

        return ToPlace(placeRootChat);
    }

    public virtual async Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var (session, placeId, expectedVersion, placeChange) = command;
        var chatChange = new Change<ChatDiff> {
            Create = placeChange.Create.HasValue ? Option<ChatDiff>.Some(ToChatDiff(placeChange.Create.Value)) : default,
            Update = placeChange.Update.HasValue ? Option<ChatDiff>.Some(ToChatDiff(placeChange.Update.Value)) : default,
            Remove = placeChange.Remove
        };
        var chatId = placeId.ToRootChatId();
        var chatChangeCommand = new Chats_Change(session, chatId, expectedVersion, chatChange);

        var placeRootChat = await Commander.Call(chatChangeCommand, true, cancellationToken).ConfigureAwait(false);
        var place = ToPlace(placeRootChat);
        return place;
    }

    public virtual async Task OnJoin(Places_Join command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return; // It just spawns other commands, so nothing to do here

        var (session, placeId, avatarId) = command;

        var joinCommand = new Authors_Join(session, placeId.ToRootChatId(), avatarId);
        await Commander.Call(joinCommand, true, cancellationToken).ConfigureAwait(false);
    }

    private Place ToPlace(Chat chat)
    {
        if (!chat.Id.IsPlaceChat(out var placeChatId) || !placeChatId.IsRoot)
            throw StandardError.Constraint("Place root chat expected");

        return new Place(placeChatId.PlaceId, chat.Version) {
            CreatedAt = chat.CreatedAt,
            IsPublic = chat.IsPublic,
            Title = chat.Title,
            MediaId = chat.MediaId,
            Picture = chat.Picture,
        };
    }

    private ChatDiff ToChatDiff(PlaceDiff placeDiff)
        => new() {
            IsPublic = placeDiff.IsPublic,
            Title = placeDiff.Title,
            Kind = ChatKind.Place,
            MediaId = placeDiff.MediaId,

            AllowGuestAuthors = null,
            AllowAnonymousAuthors = null,
            IsTemplate = null,
            TemplateId = Option<ChatId?>.None,
            TemplatedForUserId = Option<UserId?>.None,
        };
}
