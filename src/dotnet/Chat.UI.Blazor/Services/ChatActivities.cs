using System.Reactive.Linq;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatActivityKind
{
    Recording = 0,
    Typing,
}

public record ChatActivityEntry(Symbol AuthorId, ChatActivityKind Kind, Moment StartedAt);

public class ChatActivityState : IDisposable
{
    private readonly CancellationTokenSource _updateStateCancellation;
    public IMutableState<ImmutableList<ChatActivityEntry>> CurrentActivity { get; }

    internal CancellationToken Token => _updateStateCancellation.Token;
    public ChatActivityState(IMutableState<ImmutableList<ChatActivityEntry>> currentActivity)
    {
        _updateStateCancellation = new CancellationTokenSource();
        CurrentActivity = currentActivity;
    }

    public void Dispose()
        => _updateStateCancellation.CancelAndDisposeSilently();
}

public class ChatActivities
{
    private readonly IStateFactory _stateFactory;
    private Session Session { get; }
    public IChats Chats { get; }
    public MomentClockSet Clocks { get; }

    public ChatActivities(Session session, IStateFactory stateFactory, IChats chats, MomentClockSet clocks)
    {
        Session = session;
        Chats = chats;
        Clocks = clocks;
        _stateFactory = stateFactory;
    }

    // ReSharper disable once HeapView.CanAvoidClosure
    public ChatActivityState GetRecordingActivity(Symbol chatId)
    {
        using var _ = ExecutionContextExt.SuppressFlow();
        var mutableState = _stateFactory.NewMutable(ImmutableList<ChatActivityEntry>.Empty);
        var state = new ChatActivityState(mutableState);

        Task.Run(() => UpdateActivityState(chatId, mutableState, state.Token).ConfigureAwait(false), state.Token);

        return state;
    }

    // public List<ChatActivityEntry>

    private async Task UpdateActivityState(
        Symbol chatId,
        IMutableState<ImmutableList<ChatActivityEntry>> activityState,
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
        var entryUpdates = reader
            .ReadAllUpdates(
                startId,
                (tile, hasNext) => !hasNext || tile.Entries.Any(e => e.IsStreaming),
                cancellationToken)
            .Where(entry => (entry.IsStreaming && activeEntries.TryAdd(entry.Id, entry.AuthorId)) || activeEntries.ContainsKey(entry.Id));
        // var (newEntries, completedEntries) = entryUpdates.Split(e => e.IsStreaming, cancellationToken);
        // var delayedCompletedEntries = completedEntries.Delay(
        //     TimeSpan.FromMilliseconds(500),
        //     Clocks.CpuClock,
        //     cancellationToken);

        await foreach (var buffer in entryUpdates.Buffer(TimeSpan.FromMilliseconds(200), Clocks.CpuClock, cancellationToken).ConfigureAwait(false)) {
            foreach (var entry in buffer.Where(entry => !entry.IsStreaming))
                activeEntries.TryRemove(entry.Id, out _);

            var currentActivity = activityState.Value;
            var activeAuthors = activeEntries.Values.ToHashSet();
            foreach (var activityEntry in currentActivity)
                if (!activeAuthors.Remove(activityEntry.AuthorId))
                    currentActivity = currentActivity.Remove(activityEntry);

            currentActivity = currentActivity.AddRange(activeAuthors.Select(authorId
                => new ChatActivityEntry(authorId, ChatActivityKind.Recording, clock.Now)));

            if (currentActivity == activityState.Value)
                continue;

            activityState.Value = currentActivity;
        }
    }
}
