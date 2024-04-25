namespace ActualChat.Chat.UI.Blazor.Services;

// NOTE(AY): This type can't be tagged as IComputeService, coz it has a few fields,
// so we tag the implementation instead
public interface IChatStreamingActivity : IDisposable
{
    ChatActivity Owner { get; }
    ChatId ChatId { get; }
    IState<Moment?> LastTranscribedAt { get; } // Server time

    [ComputeMethod]
    Task<ImmutableList<ChatEntry>> GetStreamingEntries(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> GetStreamingAuthorIds(CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsAuthorStreaming(AuthorId authorId, CancellationToken cancellationToken);
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatStreamingActivity : WorkerBase, IChatStreamingActivity, IComputeService
{
    public static readonly TimeSpan ExtraActivityDuration = TimeSpan.FromMilliseconds(250);

    private readonly IMutableState<Moment?> _lastTranscribedAt;
    private ChatEntryReader? _textEntryReader;
    private ChatEntryReader? _audioEntryReader;
    private volatile ImmutableList<ChatEntry> _activeEntries = ImmutableList<ChatEntry>.Empty;
    private IMomentClock? _serverClock;
    private ILogger? _log;

    public ChatActivity Owner { get; }
    public ChatId ChatId { get; internal set; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<Moment?> LastTranscribedAt => _lastTranscribedAt;

    private ChatUIHub Hub { get; }
    private IMomentClock ServerClock => _serverClock ??= Hub.Clocks().ServerClock;
    private ILogger Log => _log ??= Hub.LogFor(GetType());

    public ChatEntryReader TextEntryReader
        => _textEntryReader ??= Hub.Chats.NewEntryReader(Hub.Session(), ChatId, ChatEntryKind.Text);
    public ChatEntryReader AudioEntryReader
        => _audioEntryReader ??= Hub.Chats.NewEntryReader(Hub.Session(), ChatId, ChatEntryKind.Audio);

    public ChatStreamingActivity(ChatActivity owner)
    {
        Owner = owner;
        Hub = owner.Hub;
         _lastTranscribedAt = Hub.StateFactory().NewMutable(
             (Moment?)Moment.MinValue,
             StateCategories.Get(GetType(), nameof(LastTranscribedAt)));
    }

    // [ComputeMethod]
    public virtual Task<ImmutableList<ChatEntry>> GetStreamingEntries(CancellationToken cancellationToken)
        => Task.FromResult(_activeEntries);

    // [ComputeMethod]
    public virtual Task<ApiArray<AuthorId>> GetStreamingAuthorIds(CancellationToken cancellationToken)
        => Task.FromResult(_activeEntries.Select(e => e.AuthorId).Distinct().ToApiArray());

    // [ComputeMethod]
    public virtual Task<bool> IsAuthorStreaming(AuthorId authorId, CancellationToken cancellationToken)
        => Task.FromResult(_activeEntries.Any(e => e.AuthorId == authorId));

    // Protected & private methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            PushStreamingEntries(ChatEntryKind.Audio, cancellationToken),
            PushStreamingEntries(ChatEntryKind.Text, cancellationToken)
            ).ConfigureAwait(false);
    }

    protected override Task OnStop()
    {
        foreach (var entry in _activeEntries)
            RemoveEntry(entry);
        return Task.CompletedTask;
    }

    private async Task PushStreamingEntries(ChatEntryKind entryKind, CancellationToken cancellationToken)
    {
        var startAt = Hub.Clocks().ServerClock.Now;
        var entryReader = GetEntryReader(entryKind);
        var idRange = await Hub.Chats
            .GetIdRange(Hub.Session(), ChatId, entryKind, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await entryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.LocalId ?? idRange.End;

        var entries = entryReader.Observe(startId, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.EndsAt < startAt || !entry.IsStreaming || entry.AuthorId.IsNone)
                continue;

            AddEntry(entry);
            _ = BackgroundTask.Run(async () => {
                try {
                    using var maxDurationTokenSource = new CancellationTokenSource(Constants.Chat.MaxEntryDuration);
                    using var commonTokenSource = cancellationToken.LinkWith(maxDurationTokenSource.Token);
                    await entryReader.GetWhen(
                        entry.LocalId,
                        e => e is not { IsStreaming: true },
                        commonTokenSource.Token
                    ).ConfigureAwait(false);
                    await Task.Delay(ExtraActivityDuration, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) {
                    if (e is not OperationCanceledException)
                        Log.LogError(e, "Error while waiting for entry streaming completion");
                    // We should catch every exception here
                }
                finally {
                    RemoveEntry(entry);
                }
            }, CancellationToken.None);
        }
    }

    private ChatEntryReader GetEntryReader(ChatEntryKind entryKind)
        => entryKind switch {
            ChatEntryKind.Text => TextEntryReader,
            ChatEntryKind.Audio => AudioEntryReader,
            _ => throw new ArgumentOutOfRangeException(nameof(entryKind), entryKind, null)
        };

    private void AddEntry(ChatEntry entry)
    {
        int thisAuthorEntryCount;
        lock (Lock) {
            _lastTranscribedAt.Value = null;
            _activeEntries = _activeEntries.Add(entry);
            thisAuthorEntryCount = _activeEntries.Count(e => e.AuthorId == entry.AuthorId);
        }
        using (InvalidationMode.Begin()) {
            _ = GetStreamingEntries(default);
            if (thisAuthorEntryCount == 1) {
                _ = GetStreamingAuthorIds(default);
                _ = IsAuthorStreaming(entry.AuthorId, default);
            }
        }
    }

    private void RemoveEntry(ChatEntry entry)
    {
        int thisAuthorEntryCount;
        lock (Lock) {
            _activeEntries = _activeEntries.Remove(entry);
            if (_activeEntries.IsEmpty)
                _lastTranscribedAt.Value = ServerClock.Now;
            thisAuthorEntryCount = _activeEntries.Count(e => e.AuthorId == entry.AuthorId);
        }
        using (InvalidationMode.Begin()) {
            _ = GetStreamingEntries(default);
            if (thisAuthorEntryCount == 0) {
                _ = GetStreamingAuthorIds(default);
                _ = IsAuthorStreaming(entry.AuthorId, default);
            }
        }
    }
}
