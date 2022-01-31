using System.Reactive.Linq;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ActivityKind
{
    Recording = 0,
    Typing,
}

public record ChatActivityEntry(Symbol AuthorId, ActivityKind Kind, Moment StartedAt);

public record HistoricalChatActivityEntry(Symbol AuthorId, ActivityKind Kind, Moment StartedAt, Moment EndedAt)
    : ChatActivityEntry(AuthorId, Kind, StartedAt)
{
    public HistoricalChatActivityEntry(ChatActivityEntry Entry, Moment EndedAt) : this(Entry.AuthorId,
        Entry.Kind,
        Entry.StartedAt,
        EndedAt)
    { }
}

public record ChatActivityState(
    ImmutableList<ChatActivityEntry> Current,
    ImmutableQueue<HistoricalChatActivityEntry> History)
{
    public ChatActivityState() : this(
        ImmutableList<ChatActivityEntry>.Empty,
        ImmutableQueue<HistoricalChatActivityEntry>.Empty)
    { }

    public ImmutableList<ChatActivityEntry> NotOlderThan(Moment moment)
        => Current.AddRange(History.Where(e => e.EndedAt >= moment));
}

public class ChatActivity
{
    private readonly IStateFactory _stateFactory;

    private readonly ConcurrentDictionary<Symbol, IMutableState<ChatActivityState>> _chatState = new ();
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
    public IMutableState<ChatActivityState> GetRecordingActivity(Symbol chatId, CancellationToken cancellationToken)
        => _chatState.GetOrAdd(chatId,
            cid => {
                var state = _stateFactory.NewMutable(new ChatActivityState());
                Task.Run(() => UpdateActivityState(cid, state, cancellationToken), cancellationToken);
                return state;
            });

    // public List<ChatActivityEntry>

    private async Task UpdateActivityState(
        Symbol chatId,
        IMutableState<ChatActivityState> activityState,
        CancellationToken cancellationToken)
    {
        var clock = Clocks.SystemClock;
        var startAt = clock.Now;
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

        var newAuthors = new List<Symbol>();
        var removedAuthors = new HashSet<Symbol>();
        foreach (var window in newEntries.Merge(delayedCompletedEntries)
                     .ToObservable()
                     .Buffer(TimeSpan.FromMilliseconds(200))) {
            if (window.Count == 0)
                 continue;

            var activeEntriesHaveBeenChanged = false;
            foreach (var entry in window)
                if (entry.IsStreaming) {
                    var entryAdded = activeEntries.TryAdd(entry.Id, entry.AuthorId);
                    if (entryAdded)
                        newAuthors.Add(entry.AuthorId);

                    activeEntriesHaveBeenChanged |= entryAdded;
                }
                else {
                    var entryRemoved = activeEntries.TryRemove(entry.Id, out _);
                    if (entryRemoved)
                        removedAuthors.Add(entry.AuthorId);

                    activeEntriesHaveBeenChanged |= entryRemoved;
                }

            if (activeEntriesHaveBeenChanged) {
                var state = activityState.Value;
                var current = state.Current;
                var historical = state.History;
                foreach (var entry in current)
                    if (removedAuthors.Contains(entry.AuthorId)) {
                        var now = clock.Now;
                        historical = historical.Enqueue(new HistoricalChatActivityEntry(entry, now));
                        if (historical.Peek().EndedAt < now - TimeSpan.FromMinutes(15))
                            historical = historical.Dequeue();

                        current = current.Remove(entry);
                    }

                current = current.AddRange(newAuthors.Select(authorId
                    => new ChatActivityEntry(authorId, ActivityKind.Recording, clock.Now)));

                activityState.Value = new ChatActivityState(current, historical);

                newAuthors.Clear();
                removedAuthors.Clear();
            }
        }
    }
}
