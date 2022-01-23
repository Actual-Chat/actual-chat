using System.Reactive.Linq;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ActivityKind
{
    Recording = 0,
    Typing,
}

public record ChatActivityEntry(Symbol AuthorId, ActivityKind Kind);

public class ChatActivity
{
    private readonly IStateFactory _stateFactory;

    private readonly ConcurrentDictionary<Symbol, IMutableState<ImmutableList<ChatActivityEntry>>> _chatState = new ();
    private Session Session { get; }
    public IChats Chats { get; }
    public MomentClockSet Clocks { get; }


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
        var startAt = Clocks.SystemClock.Now;
        var reader = Chats.CreateEntryReader(Session, chatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
        var startEntry = await reader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var activeEntries = new ConcurrentDictionary<long, Symbol>();
        var entryUpdates = reader.ReadAllUpdates(
            startId,
            tuple => !tuple.HasNext || tuple.Tile.Entries.Any(e => e.IsStreaming),
            entry => entry.IsStreaming || activeEntries.ContainsKey(entry.Id),
            cancellationToken);
        var (newEntries, completedEntries) = entryUpdates.Split(e => e.IsStreaming, cancellationToken);
        var delayedCompletedEntries = completedEntries.Delay(
            TimeSpan.FromMilliseconds(500),
            Clocks.CpuClock,
            cancellationToken);

        foreach (var window in newEntries.Merge(delayedCompletedEntries)
                     .ToObservable()
                     .Buffer(TimeSpan.FromMilliseconds(200))) {
            if (window.Count == 0)
                 continue;

            var activeEntriesHaveBeenChanged = false;
            foreach (var entry in window)
                activeEntriesHaveBeenChanged |= entry.IsStreaming
                    ? activeEntries.TryAdd(entry.Id, entry.AuthorId)
                    : activeEntries.TryRemove(entry.Id, out _);

            if (activeEntriesHaveBeenChanged)
                activityState.Value = activeEntries
                    .Select(e => e.Value)
                    .Distinct()
                    .Select(authorId => new ChatActivityEntry(authorId, ActivityKind.Recording))
                    .ToImmutableList();
        }
    }
}
