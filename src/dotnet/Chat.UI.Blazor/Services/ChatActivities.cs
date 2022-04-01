using ActualChat.Pooling;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatActivityKind
{
    Recording = 0,
    Typing,
}

public record ChatActivityEntry(Symbol AuthorId, ChatActivityKind Kind, Moment StartedAt);

public class ChatActivityState : IDisposable
{
    private readonly IDisposable _lease;
    public IMutableState<ImmutableList<ChatActivityEntry>> CurrentActivity { get; }

    internal ChatActivityState(IDisposable lease, IMutableState<ImmutableList<ChatActivityEntry>> state)
    {
        _lease = lease;
        CurrentActivity = state;
    }

    public void Dispose()
        => _lease.Dispose();
}

public class AuthorChatActivityState : IDisposable
{
    private readonly IDisposable _lease;
    public IMutableState<bool> Recording { get; }

    internal AuthorChatActivityState(IDisposable lease, IMutableState<bool> state)
    {
        _lease = lease;
        Recording = state;
    }

    public void Dispose()
        => _lease.Dispose();
}

public class ChatActivities
{
    private static readonly HashSet<Symbol> _emptyHashSet = new ();
    private readonly IStateFactory _stateFactory;
    private readonly SharedResourcePool<Symbol, ChatActivityStateWorker> _activeStatePool;
    private Session Session { get; }
    public IChats Chats { get; }
    public MomentClockSet Clocks { get; }

    public ChatActivities(Session session, IStateFactory stateFactory, IChats chats, MomentClockSet clocks)
    {
        Session = session;
        Chats = chats;
        Clocks = clocks;
        _stateFactory = stateFactory;
        _activeStatePool = new SharedResourcePool<Symbol, ChatActivityStateWorker>(CreateStateWorker, 2 * 60);
    }

    public async Task<ChatActivityState> GetRecordingActivity(Symbol chatId)
    {
        using var _ = ExecutionContextExt.SuppressFlow();

        var lease = await _activeStatePool.Rent(chatId).ConfigureAwait(false);
        var state = lease.Resource.State;
        return new ChatActivityState(lease, state);
    }

    public async Task<AuthorChatActivityState> GetAuthorRecordingActivity(Symbol chatId, Symbol authorId)
    {
        using var _ = ExecutionContextExt.SuppressFlow();

        var lease = await _activeStatePool.Rent(chatId).ConfigureAwait(false);
        var authorRecordingState = lease.Resource.GetAuthorRecordingState(authorId);
        return new AuthorChatActivityState(lease, authorRecordingState);
    }

    private Task<ChatActivityStateWorker> CreateStateWorker(Symbol chatId)
        => Task.FromResult(new ChatActivityStateWorker(this, chatId));

    [ComputeMethod]
    protected virtual async Task<HashSet<Symbol>> GetRecordingActivityInternal(
        Symbol chatId,
        CancellationToken cancellationToken)
    {
        var clock = Clocks.SystemClock;
        var startAt = clock.Now;
        var reader = Chats.CreateEntryReader(Session, chatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, chatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
        var startEntry = await reader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null)
            return _emptyHashSet;

        var startId = startEntry.Id;
        var endId = idRange.End;

        var activeAuthors = new HashSet<Symbol>();
        var recentEntries = reader.ReadAll(new Range<long>(startId, endId), cancellationToken);
        await foreach (var chatEntry in recentEntries.ConfigureAwait(false))
            if (chatEntry.IsStreaming)
                activeAuthors.Add(chatEntry.AuthorId);

        return activeAuthors.Count > 0
            ? activeAuthors
            : _emptyHashSet;
    }

    private class ChatActivityStateWorker : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<Symbol, WeakReference<IMutableState<bool>>> _authorRecordingState;
        private ChatActivities Owner { get; }
        private Session Session => Owner.Session;
        private MomentClockSet Clocks => Owner.Clocks;
        private IChats Chats => Owner.Chats;

        public IMutableState<ImmutableList<ChatActivityEntry>> State { get; }

        public ChatActivityStateWorker(ChatActivities owner, Symbol chatId)
        {
            Owner = owner;
            State = Owner._stateFactory.NewMutable(ImmutableList<ChatActivityEntry>.Empty);

            _cts = new CancellationTokenSource();
            _authorRecordingState = new ConcurrentDictionary<Symbol, WeakReference<IMutableState<bool>>>();
            Task.Run(() => UpdateActivityState(chatId, _cts.Token), _cts.Token);
        }

        public void Dispose()
            => _cts.CancelAndDisposeSilently();

        public IMutableState<bool> GetAuthorRecordingState(Symbol authorId)
        {
            while (true) {
                var weakState = _authorRecordingState.AddOrUpdate(
                    authorId,
                    _ => new WeakReference<IMutableState<bool>>(Owner._stateFactory.NewMutable(false)),
                    (symbol, reference) => reference.TryGetTarget(out _)
                        ? reference
                        : new WeakReference<IMutableState<bool>>(Owner._stateFactory.NewMutable(false)));

                if (weakState.TryGetTarget(out var state))
                    return state;
            }
        }

        private async Task UpdateActivityState(Symbol chatId, CancellationToken cancellationToken)
        {
            var activityState = State;
            var clock = Clocks.SystemClock;
            var activeAuthorsComputed = await Computed
                .Capture(ct => Owner.GetRecordingActivityInternal(chatId, ct), cancellationToken)
                .ConfigureAwait(false);

            var activeAuthors = activeAuthorsComputed.Value;
            if (activeAuthors.Count > 0) {
                activityState.Value = activeAuthors
                    .Select(aid => new ChatActivityEntry(aid, ChatActivityKind.Recording, clock.Now))
                    .ToImmutableList();

                foreach (var authorId in activeAuthors)
                    GetAuthorRecordingState(authorId).Value = true;
            }

            var authorStatesToRemove = new List<Symbol>();
            var previousCheckMoment = Moment.EpochStart;
            var throttled = false;
            while (true) {
                authorStatesToRemove.Clear();
                var prevActiveAuthors = activeAuthors;
                var whenInvalidatedTask = activeAuthorsComputed.WhenInvalidated(cancellationToken);
                if (throttled) {
                    throttled = false;
                    whenInvalidatedTask = whenInvalidatedTask.WithTimeout(TimeSpan.FromMilliseconds(250), cancellationToken);
                }
                await whenInvalidatedTask.ConfigureAwait(false);
                activeAuthorsComputed = await activeAuthorsComputed.Update(cancellationToken).ConfigureAwait(false);
                activeAuthors = activeAuthorsComputed.Value;
                if (prevActiveAuthors.SetEquals(activeAuthors))
                    continue;

                var now = clock.Now;
                if (now - previousCheckMoment < TimeSpan.FromMilliseconds(250)) {
                    throttled = true;
                    continue;
                }

                // calculating chat recording state
                var activeAuthors1 = activeAuthors;
                var prevAuthors = activityState.Value
                    .Select(e => e.AuthorId)
                    .ToHashSet();
                var currentActivity = activityState.Value
                    .RemoveAll(e => !activeAuthors1.Contains(e.AuthorId))
                    .AddRange(activeAuthors
                        .Except(prevAuthors)
                        .Select(authorId => new ChatActivityEntry(authorId, ChatActivityKind.Recording, now)));

                // maintaining author recording state
                foreach (var pair in _authorRecordingState) {
                    var (key, weakState) = pair;
                    if (weakState.TryGetTarget(out var state)) {
                        var original = state.Value;
                        var current = activeAuthors.Contains(key);
                        if (original != current)
                            state.Value = current;
                    }
                    else
                        authorStatesToRemove.Add(key);
                }
                // cleanup unused author recording states
                if (authorStatesToRemove.Count > _authorRecordingState.Count / 8)
                    foreach (var authorId in authorStatesToRemove)
                        _authorRecordingState.TryRemove(authorId, out _);

                activityState.Value = currentActivity;
                previousCheckMoment = now;
            }
            // ReSharper disable once FunctionNeverReturns
        }
    }
}
