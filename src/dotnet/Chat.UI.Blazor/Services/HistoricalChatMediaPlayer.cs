namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class HistoricalChatMediaPlayer : ChatMediaPlayer
{
    public override bool IsRealTimePlayer => false;

    public HistoricalChatMediaPlayer(IServiceProvider services) : base(services)
    { }

    public async Task PlaySequentially(ChatEntry chatEntry, TimeSpan skipTo)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);

        var startAt = chatEntry.BeginsAt + skipTo;
        var playTask = MediaPlayer.Play();
        var cancellationToken = MediaPlayer.StopToken;

        try {
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var startEntryId = chatEntry.Id;

            var entries = entryReader
                .GetAllAfter(startEntryId, IsRealTimePlayer, cancellationToken)
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (entry.EndsAt < startAt)
                    continue;

                var actualSkipTo = chatEntry.Id == entry.Id
                    ? skipTo
                    : TimeSpan.Zero;

                var trackId = await EnqueuePlayback(entry, actualSkipTo, cancellationToken).ConfigureAwait(false);
                if (!trackId.HasValue)
                    continue;

                var completedComputed = await Computed
                    .Capture(ct => MediaPlayerService.IsPlaybackCompleted(trackId.Value, ct), cancellationToken)
                    .ConfigureAwait(false);

                while (true) {
                    await completedComputed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                    completedComputed = await completedComputed
                        .Update(cancellationToken)
                        .ConfigureAwait(false);
                    var isPlaybackCompleted = completedComputed.Value;
                    if (isPlaybackCompleted)
                        break;
                }
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
