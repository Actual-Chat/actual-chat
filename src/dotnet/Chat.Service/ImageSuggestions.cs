using ActualChat.Chat.ML;
using ActualChat.Chat.Module;
using ActualChat.Media;

namespace ActualChat.Chat;

public class ImageSuggestions(IServiceProvider services) : IImageSuggestions
{
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;

    private IServiceProvider Services { get; } = services;
    private ChatSettings Settings { get; } = services.GetRequiredService<ChatSettings>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IChatImageDescriber ChatImageDescriber => field ??= Services.GetRequiredService<IChatImageDescriber>();
    private IImageSuggestionsBackend Backend => field ??= Services.GetRequiredService<IImageSuggestionsBackend>();
    private ICommander Commander => field ??= Services.Commander();

    // [ComputeMethod]
    public virtual async Task<ImageSuggestion?> GetForChat(
        Session session,
        ChatId chatId,
        ImageSlot slot,
        CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            return null;

        return await Backend.Get(GetKey(chatId, slot), cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Moment?> GetDismissedUntilForChat(
        Session session,
        ChatId chatId,
        ImageSlot slot,
        CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            return null;

        return await Backend.GetDismissedUntil(GetKey(chatId, slot), cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<Moment?> GetGenerationStartedAtForChat(
        Session session,
        ChatId chatId,
        ImageSlot slot,
        CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            return null;

        return await Backend.GetGenerationStartedAt(GetKey(chatId, slot), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ImageSuggestion?> GetForPlace(
        Session session, PlaceId placeId, ImageSlot slot, CancellationToken cancellationToken)
    {
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        if (!rules.IsOwner())
            return null;

        return await Backend.Get(GetKey(placeId, slot), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<Moment?> GetDismissedUntilForPlace(
        Session session, PlaceId placeId, ImageSlot slot, CancellationToken cancellationToken)
    {
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        if (!rules.IsOwner())
            return null;

        return await Backend.GetDismissedUntil(GetKey(placeId, slot), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> CanGenerateForPlace(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        if (!rules.IsOwner())
            return false;

        var chats = await ListPlaceChats(session, placeId, cancellationToken).ConfigureAwait(false);
        return chats.Count >= 2;
    }

    // [CommandHandler]
    public virtual async Task<ImageSuggestion?> OnGenerateForChat(
        ImageSuggestions_GenerateForChat command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, chatId, slot, imageDescription) = command;
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            throw StandardError.Unauthorized("You can't change this chat's picture.");

        var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        if (imageDescription.IsNullOrEmpty()) {
            imageDescription = await Describe(chat, cancellationToken).ConfigureAwait(false);
            if (imageDescription.IsNullOrEmpty())
                return null; // Nothing to describe yet, or the describer is disabled
        }

        // The picker is a UI setting, so the client sends it; the banner's own trigger has none yet
        var style = command.Style ?? ImageStyle.Default;
        var generate = new ImageSuggestionsBackend_Generate(GetKey(chatId, slot), imageDescription) {
            MediaKind = MediaKind.ChatPicture,
            IsExplicit = command.IsExplicit,
            Style = style,
            // A regenerate that reuses the seed would reproduce the same image
            Seed = command.IsExplicit ? Random.Shared.NextInt64(1, int.MaxValue) : null,
        };
        return await Commander.Call(generate, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnAcceptForChat(
        ImageSuggestions_AcceptForChat command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, chatId, slot) = command;
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            throw StandardError.Unauthorized("You can't change this chat's picture.");

        var key = GetKey(chatId, slot);
        var suggestion = await Backend.Get(key, cancellationToken).ConfigureAwait(false);
        if (suggestion is null)
            return;

        var change = new Chats_Change {
            Session = session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { MediaId = suggestion.MediaId }),
        };
        await Commander.Call(change, true, cancellationToken).ConfigureAwait(false);

        // The media stays: it is the chat's picture now, not a pending suggestion
        var remove = new ImageSuggestionsBackend_Remove(key, false);
        await Commander.Call(remove, true, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnDismissForChat(
        ImageSuggestions_DismissForChat command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, chatId, slot) = command;
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanEditProperties())
            throw StandardError.Unauthorized("You can't change this chat's picture.");

        var dismissedUntil = Services.Clocks().SystemClock.Now + Settings.ImageSuggestionDismissPeriod;
        var dismiss = new ImageSuggestionsBackend_Dismiss(GetKey(chatId, slot), dismissedUntil);
        await Commander.Call(dismiss, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ImageSuggestion?> OnGenerateForPlace(
        ImageSuggestions_GenerateForPlace command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default!;

        var (session, placeId, slot, imageDescription) = command;
        var key = GetKey(placeId, slot);
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        rules.Require(PlacePermissions.Owner);
        var place = await Places.Get(session, placeId, cancellationToken).Require().ConfigureAwait(false);
        var chats = await ListPlaceChats(session, placeId, cancellationToken).ConfigureAwait(false);
        if (chats.Count < 2)
            return null;

        if (!command.IsExplicit) {
            if ((slot == ImageSlot.Picture ? place.MediaId : place.BackgroundMediaId) != null)
                return null;

            var dismissedUntil = await Backend.GetDismissedUntil(key, cancellationToken).ConfigureAwait(false);
            if (dismissedUntil > Services.Clocks().SystemClock.Now)
                return null;

            var existing = await Backend.Get(key, cancellationToken).ConfigureAwait(false);
            if (existing != null)
                return existing;
        }

        var isBackground = slot == ImageSlot.Background;
        if (imageDescription.IsNullOrWhiteSpace()) {
            var sources = new List<ChatImageDescriptionSource>();
            foreach (var chat in chats) {
                var entries = await ListFirstTextEntries(chat.Id, cancellationToken).ConfigureAwait(false);
                sources.Add(new ChatImageDescriptionSource(chat.Title, chat.Description, entries));
            }
            imageDescription = await ChatImageDescriber
                .DescribePlace(place, sources, isBackground, cancellationToken).ConfigureAwait(false);
            if (imageDescription.IsNullOrWhiteSpace())
                return null;
        }

        var generate = new ImageSuggestionsBackend_Generate(key, imageDescription) {
            MediaKind = MediaKind.ChatPicture,
            IsBackground = isBackground,
            Width = 1024,
            Height = 1024,
            IsExplicit = command.IsExplicit,
            Style = command.Style ?? (isBackground ? ImageStyle.Photo : ImageStyle.Default),
            Seed = command.IsExplicit ? Random.Shared.NextInt64(1, int.MaxValue) : null,
        };
        return await Commander.Call(generate, true, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnAcceptForPlace(
        ImageSuggestions_AcceptForPlace command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, placeId, slot) = command;
        var key = GetKey(placeId, slot);
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        rules.Require(PlacePermissions.Owner);
        var suggestion = await Backend.Get(key, cancellationToken).ConfigureAwait(false);
        if (suggestion is null)
            return;

        var place = await Places.Get(session, placeId, cancellationToken).Require().ConfigureAwait(false);
        if ((slot == ImageSlot.Picture ? place.MediaId : place.BackgroundMediaId) != null)
            return;

        var diff = slot == ImageSlot.Picture
            ? new PlaceDiff { MediaId = suggestion.MediaId }
            : new PlaceDiff { BackgroundMediaId = suggestion.MediaId };
        await Commander.Call(new Places_Change {
            Session = session,
            PlaceId = placeId,
            ExpectedVersion = place.Version,
            Change = Change.Update(diff),
        }, true, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ImageSuggestionsBackend_Remove(key, false), true, cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task OnDismissForPlace(
        ImageSuggestions_DismissForPlace command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (session, placeId, slot) = command;
        var key = GetKey(placeId, slot);
        var rules = await Places.GetRules(session, placeId, cancellationToken).ConfigureAwait(false);
        rules.Require(PlacePermissions.Owner);
        var dismissedUntil = Services.Clocks().SystemClock.Now + Settings.ImageSuggestionDismissPeriod;
        await Commander.Call(new ImageSuggestionsBackend_Dismiss(key, dismissedUntil), true, cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task<IReadOnlyList<Chat>> ListPlaceChats(
        Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var chatIds = await ChatsBackend.ListPlaceChatIds(placeId, cancellationToken).ConfigureAwait(false);
        var chats = new List<Chat>();
        foreach (var chatId in chatIds) {
            var chat = await Chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat is null || !chat.Rules.CanRead() || chatId.IsThread())
                continue;

            chats.Add(chat);
            if (chats.Count == 10)
                break;
        }
        return chats;
    }

    private async Task<IReadOnlyCollection<ChatEntrySlim>> ListFirstTextEntries(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var range = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var entries = new List<ChatEntrySlim>();
        var nextId = range.Start;
        while (nextId < range.End && entries.Count < 10) {
            var tileRange = EntryIdTiles.GetTile(nextId).Range;
            var tile = await ChatsBackend.GetTile(chatId, tileRange, false, cancellationToken).ConfigureAwait(false);
            foreach (var entry in tile.Entries.OrderBy(x => x.LocalId)) {
                if (entry.LocalId < nextId || !range.Contains(entry.LocalId)
                    || entry.IsSystemEntry || entry.IsRemoved || entry.IsContentStreaming
                    || entry.Content.IsNullOrWhiteSpace())
                    continue;

                entries.Add(new ChatEntrySlim(entry));
                if (entries.Count == 10)
                    break;
            }
            nextId = tileRange.End;
        }
        return entries;
    }

    private static string GetKey(PlaceId placeId, ImageSlot slot)
        => slot switch {
            ImageSlot.Picture => $"picture/p:{placeId.Value}",
            ImageSlot.Background => $"background/p:{placeId.Value}",
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

    // Hand-composed for now. Once ContentRef lands this becomes $"{slot}/{chatId.ContentRef}",
    // which is the same string - that is why the prefix is spelled out here.
    private static string GetKey(ChatId chatId, ImageSlot slot)
        => $"{slot.ToString().ToLower()}/c:{chatId.Value}";

    // No "is there enough to describe this" check: the client decides when to ask, and an explicit
    // regenerate must work on a chat of any size. The describer returning "" is still a no.
    private async Task<string> Describe(Chat chat, CancellationToken cancellationToken)
    {
        var entries = await ListRecentTextEntries(chat.Id, cancellationToken).ConfigureAwait(false);
        return await ChatImageDescriber
            .Describe(chat.Title, chat.Description, entries, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ChatEntrySlim>> ListRecentTextEntries(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        var maxCount = Settings.MaxImageSuggestionEntries;
        var start = Math.Max(lidRange.Start, lidRange.End - maxCount);
        var tailRange = new Range<long>(start, lidRange.End);
        if (tailRange.IsEmpty)
            return [];

        var tiles = await EntryIdTiles
            .GetCoveringTiles(tailRange)
            .Select(idTile => ChatsBackend.GetTile(chatId, idTile.Range, false, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        return tiles
            .SelectMany(tile => tile.Entries)
            .Where(e => tailRange.Contains(e.LocalId))
            // A system entry stores no text, so it would reach the describer as a blank line
            .Where(e => !e.IsSystemEntry && !e.IsRemoved && !e.IsContentStreaming)
            .DistinctBy(e => e.LocalId)
            .OrderBy(e => e.LocalId)
            .Select(e => new ChatEntrySlim(e))
            .ToList();
    }
}
