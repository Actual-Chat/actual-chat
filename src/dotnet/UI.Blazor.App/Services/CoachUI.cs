using ActualChat.Chat;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The client side of the speech coach: the per-user verdicts, the marks behind inline marking
/// (fetched once per entry tile), and jump-to-audio from an occurrence.
/// </summary>
public class CoachUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private const double ReplayLeadSeconds = 0.25;

    private IChats Chats => Hub.Chats;
    private IChatCoach ChatCoach => Hub.ChatCoach;
    private ChatUI ChatUI => Hub.ChatUI;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;

    [ComputeMethod]
    public virtual async Task<bool> IsEnabled(CancellationToken cancellationToken)
        => await Features.Get<Features_EnableSpeechCoach>(cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<bool> IsMarkingEnabled(CancellationToken cancellationToken)
    {
        if (!await IsEnabled(cancellationToken).ConfigureAwait(false))
            return false;

        var settings = await UserSettingsUI.UserCoachSettings().Get(cancellationToken).ConfigureAwait(false);
        return settings.IsCoachingEnabled;
    }

    [ComputeMethod]
    public virtual async Task<ApiArray<SpeechSpan>> GetOwnMarks(
        ChatEntryId entryId, AuthorId authorId, CancellationToken cancellationToken)
    {
        if (!await IsMarkingEnabled(cancellationToken).ConfigureAwait(false))
            return ApiArray<SpeechSpan>.Empty;

        var chat = await Chats.Get(Session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.Author?.Id != authorId)
            return ApiArray<SpeechSpan>.Empty;

        var tile = Constants.Chat.EntryIdTiles.GetTile(entryId.LocalId);
        var marks = await GetOwnMarksTile(entryId.ChatId, tile.Range, cancellationToken).ConfigureAwait(false);
        foreach (var mark in marks)
            if (mark.EntryLid == entryId.LocalId)
                return mark.Spans;
        return ApiArray<SpeechSpan>.Empty;
    }

    public async Task JumpTo(CoachOccurrence occurrence, CancellationToken cancellationToken)
    {
        var entryId = ChatEntryId.New(occurrence.ChatId, occurrence.EntryLid);
        // Navigation and replay need the Blazor dispatcher, so the awaits keep the context
        var entry = await ChatUI.GetEntry(entryId, cancellationToken).ConfigureAwait(true);
        if (entry is null)
            return;

        if (ChatUI.SelectedChatId.Value == occurrence.ChatId)
            ChatUI.HighlightEntry(entryId, navigate: true);
        else
            await History.NavigateTo(Links.Chat(occurrence.ChatId, occurrence.EntryLid)).ConfigureAwait(true);
        PanelsUI.HidePanels();

        if (entry.Audio?.TimeMap.TryMap(occurrence.Start) is not { } startTime)
            return;

        var startAt = entry.BeginsAt + TimeSpan.FromSeconds(startTime - ReplayLeadSeconds);
        await ChatAudioUI.StartReplay(occurrence.ChatId, startAt).ConfigureAwait(true);
    }

    // Protected methods

    // One RPC per 5-lid tile; the entries of a tile share it through the compute cache
    [ComputeMethod]
    protected virtual Task<ApiArray<CoachEntryMarks>> GetOwnMarksTile(
        ChatId chatId, Range<long> lidTileRange, CancellationToken cancellationToken)
        => ChatCoach.GetOwnMarks(Session, chatId, lidTileRange, cancellationToken);
}
