namespace ActualChat.Chat.UI.Blazor.Services;

public interface IChatRecordingActivity : IDisposable
{
    ChatActivity Owner { get; }
    Symbol ChatId { get; }

    Task<ImmutableList<ChatEntry>> GetActiveChatEntries(CancellationToken cancellationToken);
    Task<ImmutableArray<Symbol>> GetActiveAuthorIds(CancellationToken cancellationToken);
    // NOTE(AY): authorId is string to avoid boxing in [ComputeMethod]
    Task<bool> IsAuthorActive(string authorId, CancellationToken cancellationToken);
}

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class ChatRecordingActivity : WorkerBase, IChatRecordingActivity
{
    public static readonly TimeSpan ExtraActivityDuration = TimeSpan.FromMilliseconds(250);

    private readonly ILogger _log;
    private ChatEntryReader? _entryReader;
    private volatile ImmutableList<ChatEntry> _activeEntries = ImmutableList<ChatEntry>.Empty;

    public ChatActivity Owner { get; }
    public Symbol ChatId { get; internal set; }
    public ChatEntryReader EntryReader
        => _entryReader ??= Owner.Chats.NewEntryReader(Owner.Session, ChatId, ChatEntryType.Audio);

    public ChatRecordingActivity(ChatActivity owner)
    {
        Owner = owner;
        _log = owner.Services.LogFor(GetType());
    }

    [ComputeMethod]
    public virtual Task<ImmutableList<ChatEntry>> GetActiveChatEntries(CancellationToken cancellationToken)
        => Task.FromResult(_activeEntries);

    [ComputeMethod]
    public virtual Task<ImmutableArray<Symbol>> GetActiveAuthorIds(CancellationToken cancellationToken)
        => Task.FromResult(_activeEntries.Select(e => e.AuthorId).ToImmutableArray());

    [ComputeMethod]
    public virtual Task<bool> IsAuthorActive(string authorId, CancellationToken cancellationToken)
    {
        var sAuthorId = (Symbol) authorId;
        return Task.FromResult(_activeEntries.Any(e => e.AuthorId == sAuthorId));
    }

    // Protected & private methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var startAt = Owner.Clocks.SystemClock.Now;
        var idRange = await Owner.Chats.GetIdRange(Owner.Session, ChatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await EntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var entries = EntryReader.ReadAllWaitingForNew(startId, cancellationToken);
        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (entry.EndsAt < startAt || !entry.IsStreaming || entry.AuthorId.IsEmpty)
                continue;
            _ = Task.Run(() => ProcessEntry(entry, cancellationToken), cancellationToken);
        }
    }

    private async Task ProcessEntry(ChatEntry entry, CancellationToken cancellationToken)
    {
        var authorId = entry.AuthorId;
        bool isAuthorSetChanged;
        lock (Lock) {
            isAuthorSetChanged = _activeEntries.All(e => e.AuthorId != authorId);
            _activeEntries = _activeEntries.Add(entry);
        }
        using (Computed.Invalidate()) {
            _ = GetActiveChatEntries(default);
            if (isAuthorSetChanged) {
                _ = GetActiveAuthorIds(default);
                _ = IsAuthorActive(authorId, default);
            }
        }

        try {
            var idTile = Constants.Chat.IdTileStack.FirstLayer.GetTile(entry.Id);
            var cTile = await Computed.Capture(
                ct => Owner.Chats.GetTile(Owner.Session, ChatId, EntryReader.EntryType, idTile.Range, ct),
                cancellationToken
                ).ConfigureAwait(false);

            using var maxDurationTokenSource = new CancellationTokenSource(Constants.Chat.MaxEntryDuration);
            using var commonTokenSource = cancellationToken.LinkWith(maxDurationTokenSource.Token);
            await cTile.When(
                tile => !(tile.Entries.FirstOrDefault(e => e.Id == entry.Id)?.IsStreaming ?? false),
                commonTokenSource.Token
                ).ConfigureAwait(false);
            await Owner.Clocks.CpuClock.Delay(ExtraActivityDuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e is not OperationCanceledException)
                _log.LogError(e, nameof(ProcessEntry) + " failed");
            // We should catch every exception here
        }

        lock (Lock) {
            _activeEntries = _activeEntries.Remove(entry);
            isAuthorSetChanged = _activeEntries.All(e => e.AuthorId != authorId);
        }
        using (Computed.Invalidate()) {
            _ = GetActiveChatEntries(default);
            if (isAuthorSetChanged) {
                _ = GetActiveAuthorIds(default);
                _ = IsAuthorActive(authorId, default);
            }
        }
    }
}
