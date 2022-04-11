using ActualChat.MediaPlayback;
using ActualChat.Messaging;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    public RealtimeChatPlayer(Session session, Symbol chatId, IServiceProvider services)
        : base(session, chatId, services)
        => PlayerKind = ChatPlayerKind.Realtime;

    // ReSharper disable once RedundantAssignment
    protected override async Task Play(Moment startAt, CancellationToken cancellationToken)
    {
        startAt = Clocks.SystemClock.Now; // We always override startAt here
        var cpuClock = Clocks.CpuClock;
        var audioEntryReader = Chats.NewEntryReader(Session, ChatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var entries = audioEntryReader.ReadAllWaitingForNew(startId, cancellationToken);
        var playProcesses = new ConcurrentDictionary<IMessageProcess<PlayTrackCommand>, Unit>();
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.EndsAt < startAt)
                // We're starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                // Note that streaming entries have EndsAt == null, so we don't skip them.
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var chatAuthor = await ChatAuthors.GetChatAuthor(Session, ChatId, cancellationToken)
                    .ConfigureAwait(false);
                if (chatAuthor != null && entry.AuthorId == chatAuthor.Id)
                    continue;
            }

            var skipToOffset = entry.IsStreaming ? StreamingSkipTo : TimeSpan.Zero;
            var entryBeginsAt = Moment.Max(entry.BeginsAt + skipToOffset, startAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;

            var playProcess = await EnqueueEntry(cpuClock.Now, entry, skipTo, cancellationToken).ConfigureAwait(false);
            if (playProcess.WhenCompleted.IsCompleted)
                continue;

            playProcesses.TryAdd(playProcess, default);
            _ = playProcess.WhenCompleted.ContinueWith(
                t => playProcesses.TryRemove(playProcess, out _),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        await Task.WhenAll(playProcesses.Keys.Select(s => s.WhenCompleted)).ConfigureAwait(false);
    }
}
