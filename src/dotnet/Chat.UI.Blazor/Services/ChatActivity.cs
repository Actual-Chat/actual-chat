using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ActivityKind
{
    Recording = 0,
    Typing,
}

public record ChatActivityEntry(Symbol AuthorId, ActivityKind Kind);

public class ChatActivity
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.IdTileStack;

    private readonly IStateFactory _stateFactory;

    private readonly ConcurrentDictionary<Symbol, IMutableState<ImmutableList<ChatActivityEntry>>> _chatState = new ();
    private Session Session { get; }
    public IChats Chats { get; }
    public MomentClockSet Clocks { get; }

    public TimeSpan ExtraChatTileInvalidationWaitTimeout { get; init; } = TimeSpan.FromMilliseconds(50);

    public ChatActivity(Session session, IStateFactory stateFactory, IChats chats, MomentClockSet clocks)
    {
        Session = session;
        Chats = chats;
        Clocks = clocks;
        _stateFactory = stateFactory;

    }

    // ReSharper disable once HeapView.CanAvoidClosure
    public IMutableState<ImmutableList<ChatActivityEntry>> GetRecordingActivity(Symbol chatId, CancellationToken cancellationToken)
        => _chatState.GetOrAdd(chatId,
            cid => {
                var state = _stateFactory.NewMutable(ImmutableList<ChatActivityEntry>.Empty);
                Task.Run(() => UpdateActivityState(cid, state, cancellationToken), cancellationToken);
                return state;
            });

    private async Task UpdateActivityState(
        Symbol chatId,
        IMutableState<ImmutableList<ChatActivityEntry>> activityState,
        CancellationToken cancellationToken)
    {
        var activeEntries = new HashSet<(long,Symbol)>();
        var activeEntriesSubject = Observable.Create<ChatEntry>(observer => TrackRecordingBeginning(chatId, observer, cancellationToken));
        var completedEntriesSubject = Observable.Create<ChatEntry>(observer => TrackRecordingCompletion(chatId, observer, cancellationToken))
            .Delay(TimeSpan.FromMilliseconds(500));

        foreach (var window in activeEntriesSubject.Merge(completedEntriesSubject).Buffer(TimeSpan.FromMilliseconds(200))) {
            if (window.Count == 0)
                 continue;

            var activeEntriesHaveBeenChanged = false;
            foreach (var entry in window)
                activeEntriesHaveBeenChanged |= entry.IsStreaming
                    ? activeEntries.Add((entry.Id, entry.AuthorId))
                    : activeEntries.Remove((entry.Id, entry.AuthorId));

            if (activeEntriesHaveBeenChanged)
                activityState.Value = activeEntries
                    .Select(e => e.Item2)
                    .Distinct()
                    .Select(authorId => new ChatActivityEntry(authorId, ActivityKind.Recording))
                    .ToImmutableList();
        }
    }

    private async Task TrackRecordingBeginning(
        Symbol chatId,
        IObserver<ChatEntry> observer,
        CancellationToken cancellationToken)
    {
        var startAt = Clocks.SystemClock.Now;
        var reader = Chats.CreateEntryReader(Session, chatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
        var startEntry = await reader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var entries = reader.ReadAllWaitingForNew(startId, cancellationToken);
        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (entry.EndsAt == null && !entry.IsStreaming)
                continue;

            observer.OnNext(entry);
        }
    }

    private async Task TrackRecordingCompletion(
        Symbol chatId,
        IObserver<ChatEntry> observer,
        CancellationToken cancellationToken)
    {
        var startAt = Clocks.SystemClock.Now;
        var reader = Chats.CreateEntryReader(Session, chatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
        var startEntry = await reader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);


        var idTilesLayer0 = IdTileStack.FirstLayer;
        var startTile = idTilesLayer0.GetTile(startEntry?.Id ?? idRange.End);
        var activeTiles = new Dictionary<Range<long>, (IComputed<ChatTile> Computed, long CompletedEntryId)>();
        var lastId = 0L;
        var thisTile = await Chats.GetTile(Session, chatId, ChatEntryType.Audio, startTile.Range, cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            var now = Clocks.SystemClock.Now;
            var thisTileIdRange = thisTile.IdTileRange;
            var nextTileIdRange = thisTileIdRange.Move(idTilesLayer0.TileSize);
            if (lastId >= thisTileIdRange.End - 1) {
                // We anyway have to move to the next tile
                thisTile = await Chats.GetTile(Session, chatId, ChatEntryType.Audio, nextTileIdRange, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var hasStreamingEntries = false;
            foreach (var entry in thisTile.Entries) {
                if (entry.Id <= lastId)
                    continue;

                if (entry.IsStreaming && entry.BeginsAt > now - Constants.Chat.MaxEntryDuration) {
                    hasStreamingEntries = true;
                    break;
                }

                observer.OnNext(entry);
                lastId = entry.Id;
            }
            if (!hasStreamingEntries)
                activeTiles.Remove(thisTileIdRange);

            var thisTileComputed = await Computed
                .Capture(ct => Chats.GetTile(Session,
                        chatId,
                        ChatEntryType.Audio,
                        startTile.Range,
                        ct),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!ReferenceEquals(thisTileComputed.Value, thisTile)) {
                // We've got a new version of thisTile
                thisTile = thisTileComputed.Value;
                continue;
            }
            if (hasStreamingEntries)
                activeTiles.Add(thisTileIdRange, (thisTileComputed, lastId));

            // It's still the same tile, so we need to wait for either its invalidation or the next tile
            var thisTileInvalidatedTask = thisTileComputed.WhenInvalidated(cancellationToken);
            var nextTileComputed = await Computed
                .Capture(ct => Chats.GetTile(Session, chatId, ChatEntryType.Audio, nextTileIdRange, ct), cancellationToken)
                .ConfigureAwait(false);
            var nextTileInvalidatedTask = nextTileComputed.Value.IsEmpty
                ? nextTileComputed.WhenInvalidated(cancellationToken)
                : Task.CompletedTask; //

            var invalidationTasks = activeTiles.Values
                .Select(c => c.Computed.WhenInvalidated(cancellationToken))
                .ToList();
            invalidationTasks.Add(thisTileInvalidatedTask);
            invalidationTasks.Add(nextTileInvalidatedTask);
            await Task.WhenAny(invalidationTasks).ConfigureAwait(false);

            now = Clocks.SystemClock.Now;

            foreach (var (tileIdRange, (activeTileComputed, completedEntryId)) in activeTiles) {
                if (activeTileComputed.IsConsistent())
                    continue;
                if (activeTileComputed == thisTileComputed)
                    continue;

                var lastCompletedEntryId = completedEntryId;
                var updatedTileComputed = await activeTileComputed.Update(cancellationToken).ConfigureAwait(false);

                var updatedTile = updatedTileComputed.Value;
                var updatedTileHasStreaming = false;
                foreach (var entry in updatedTile.Entries) {
                    if (entry.Id <= lastCompletedEntryId)
                        continue;

                    if (entry.IsStreaming && entry.BeginsAt > now - Constants.Chat.MaxEntryDuration) {
                        updatedTileHasStreaming = true;
                        break;
                    }

                    observer.OnNext(entry);
                    lastCompletedEntryId = entry.Id;
                }
                if (!updatedTileHasStreaming)
                    activeTiles.Remove(tileIdRange);
                else
                    activeTiles[tileIdRange] = (updatedTileComputed, lastCompletedEntryId);
            }

            if (thisTileComputed.IsConsistent()) {
                // nextTileComputed is either invalidated (i.e. likely non-empty) or non-empty here,
                // but thisTileComputed is still the same.
                // Let's give it a bit of extra time - maybe it's a rapid sequence
                // of updates that invalidated both tiles, and we just need to wait
                // a bit more to see the lastTileComputed invalidation.
                await thisTileInvalidatedTask
                    .WithTimeout(ExtraChatTileInvalidationWaitTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!thisTileComputed.IsConsistent()) {
                // thisTile was invalidated - let's update it
                thisTileComputed = await thisTileComputed.Update(cancellationToken).ConfigureAwait(false);
                thisTile = thisTileComputed.Value;
                continue;
            }

            // We gave thisTileComputed every chance to get invalidated, but it didn't happen,
            // and we know the next tile might be available, so...
            if (nextTileComputed.Value.IsEmpty) {
                // This means it was invalidated (see nextTileInvalidatedTask = ...), so let's update it first
                nextTileComputed = await nextTileComputed.Update(cancellationToken).ConfigureAwait(false);
            }
            if (!nextTileComputed.Value.IsEmpty) {
                // Next tile is ready, so we're switching to it
                thisTile = nextTileComputed.Value;
            }
            // nextTile is still empty, so let's continue watching thisTile/nextTile pair
        }
    }


}
