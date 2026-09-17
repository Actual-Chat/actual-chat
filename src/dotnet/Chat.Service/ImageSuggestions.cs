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

    // Private methods

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
