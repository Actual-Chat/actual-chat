using ActualChat.Chat.Db;
using ActualChat.Chat.Events;
using ActualChat.Media;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public class PlacesBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IPlacesBackend
{
    private IDbEntityResolver<string, DbPlace> DbPlaceResolver { get; } = services.GetRequiredService<IDbEntityResolver<string, DbPlace>>();
    private IMediaBackend MediaBackend => services.GetRequiredService<IMediaBackend>();
    private IChatsBackend ChatsBackend => services.GetRequiredService<IChatsBackend>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();

    // [ComputeMethod]
    public virtual async Task<Place?> Get(PlaceId placeId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));

        var dbPlace = await DbPlaceResolver.Get(placeId, cancellationToken).ConfigureAwait(false);
        var place = dbPlace?.ToModel();
        if (place == null)
            return null;

        if (!place.MediaId.IsNone) {
            var media = await MediaBackend.Get(place.MediaId, cancellationToken).ConfigureAwait(false);
            place = place with { Picture = media };
        }
        if (!place.BackgroundMediaId.IsNone) {
            var background = await MediaBackend.Get(place.BackgroundMediaId, cancellationToken).ConfigureAwait(false);
            place = place with { Background = background };
        }
        return place;
    }

    // [CommandHandler]
    public virtual async Task<Place> OnChange(
        PlacesBackend_Change command,
        CancellationToken cancellationToken)
    {
        var (placeId, expectedVersion, change, ownerId) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invPlace = context.Operation.Items.Get<Place>();
            if (invPlace != null)
                _ = Get(invPlace.Id, default);
            return null!;
        }

        change.RequireValid();
        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbPlace = placeId.IsNone ? null :
            await dbContext.Places.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == placeId, cancellationToken)
                .ConfigureAwait(false);
        var oldPlace = dbPlace?.ToModel();
        Place place;
        if (change.IsCreate(out var update)) {
            oldPlace.RequireNull();
            placeId.RequireNone();
            ownerId.Require();

            placeId = new PlaceId(Generate.Option);
            place = new Place(placeId) {
                CreatedAt = Clocks.SystemClock.Now,
            };
            place = ApplyDiff(place, update);
            dbPlace = new DbPlace(place);

            dbContext.Add(dbPlace);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (change.IsUpdate(out update)) {
            dbPlace.RequireVersion(expectedVersion);
            place = ApplyDiff(dbPlace.ToModel(), update);
            dbPlace.UpdateFrom(place);
        }
        else if (change.IsRemove()) {
            dbPlace.Require();

            await RemoveMedia(dbPlace.MediaId).ConfigureAwait(false);
            await RemoveMedia(dbPlace.BackgroundMediaId).ConfigureAwait(false);

            dbContext.Remove(dbPlace);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        place = dbPlace.Require().ToModel();
        context.Operation.Items.Set(place);

        long? chatExpectedVersion;
        Change<ChatDiff> chatChange;
        var rootChatId = place.Id.ToRootChatId();
        var rootChat = await ChatsBackend.Get(rootChatId, cancellationToken).ConfigureAwait(false);
        if (change.Kind == ChangeKind.Create) {
            rootChat.RequireNull();
            chatExpectedVersion = null;
            chatChange = Change.Create(ToChatDiff(place));
        }
        else {
            rootChat.Require();
            chatExpectedVersion = rootChat.Version;
            chatChange = !change.Remove ? Change.Update(ToChatDiff(place)) : Change.Remove<ChatDiff>();
        }

        // Since place root chat is still used for getting place rules and place members we should synchronously update it.
        var chatChangeCommand = new ChatsBackend_Change(rootChatId, chatExpectedVersion, chatChange);
        if (change.Kind == ChangeKind.Create)
            chatChangeCommand = chatChangeCommand with { OwnerId = ownerId };
        rootChat = await Commander.Call(chatChangeCommand, true, cancellationToken).ConfigureAwait(false);
        rootChat.Require();
        if (rootChat.Id != rootChatId)
            throw StandardError.Internal("Id of root place chat does not correspond place id");

        // Raise events
        context.Operation.AddEvent(new PlaceChangedEvent(place, oldPlace, change.Kind));
        return place;

        Place ApplyDiff(Place originalPlace, PlaceDiff? diff) {
            // Update
            var newPlace = DiffEngine.Patch(originalPlace, diff) with {
                Version = VersionGenerator.NextVersion(originalPlace.Version),
            };

            // Validation
            if (newPlace.Title.IsNullOrEmpty())
                throw StandardError.Constraint("Place title cannot be empty.");

            return newPlace;
        }

        async Task RemoveMedia(string mediaSid)
        {
            if (!mediaSid.IsNullOrEmpty()) {
                var removeMediaCommand = new MediaBackend_Change(
                    new MediaId(mediaSid),
                    new Change<Media.Media> { Remove = true });
                await Commander.Call(removeMediaCommand, true, cancellationToken).ConfigureAwait(false);
            }
        }

        static ChatDiff ToChatDiff(Place place)
            => new() {
                IsPublic = place.IsPublic,
                Title = place.Title,
                Kind = ChatKind.Place,
                MediaId = place.MediaId,
            };
    }
}
