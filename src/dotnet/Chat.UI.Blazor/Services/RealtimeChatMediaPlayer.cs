namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatMediaPlayer : ChatMediaPlayer
{
    public override bool IsRealTimePlayer => true;

    public RealtimeChatMediaPlayer(IServiceProvider services) : base(services)
    { }

    public async Task Play()
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        var playTask = MediaPlayer.Play();
        var cancellationToken = MediaPlayer.StopToken;

        try {
            var clock = Clocks.CpuClock;
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var now = clock.Now;
            var startEntryId = await entryReader
                .GetNextEntryId(now - ChatConstants.MaxEntryDuration, cancellationToken)
                .ConfigureAwait(false);

            var entries = entryReader
                .GetAllAfter(startEntryId, IsRealTimePlayer, cancellationToken)
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (entry.EndsAt < now)
                    // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                    // so we need to skip a few entries.
                    // Note that streaming entries have EndsAt == null, so we don't skip them.
                    continue;

                now = clock.Now;
                var skipTo = now > entry.BeginsAt
                    ? now - entry.BeginsAt
                    : TimeSpan.Zero;

                _ = EnqueuePlayback(entry, skipTo, cancellationToken).ConfigureAwait(false);
            }
            MediaPlayer.Complete();
            await playTask.ConfigureAwait(false);
        }
        catch {
            try {
                await MediaPlayer.Stop().ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            throw;
        }
    }
}
