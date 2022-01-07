using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    // Min. delay is ~ 2.5*Ping, so we can skip something
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.FromSeconds(0.05);

    public RealtimeChatPlayer(IServiceProvider services) : base(services) { }

    protected override async Task PlayInternal(Moment startAt, Playback playback, CancellationToken cancellationToken)
    {
        var cpuClock = Clocks.CpuClock;

        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
        var startEntry = await AudioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var entries = AudioEntryReader
            .ReadAllWaitingForNew(startId, cancellationToken)
            .Where(e => e.Type == ChatEntryType.Audio);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.EndsAt < startAt)
                // We're starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                // Note that streaming entries have EndsAt == null, so we don't skip them.
                continue;

            var chatAuthor = await GetChatAuthor(cancellationToken).ConfigureAwait(false);
            if (chatAuthor != null && entry.AuthorId == chatAuthor.Id) {
                if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio)
                    continue;
            }

            var skipToOffset = entry.IsStreaming ? StreamingSkipTo : TimeSpan.Zero;
            var entryBeginsAt = Moment.Max(entry.BeginsAt + skipToOffset, startAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;
            await EnqueueEntry(playback, cpuClock.Now, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
