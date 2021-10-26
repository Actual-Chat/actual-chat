using ActualChat.Audio;
using ActualChat.Playback;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlaybackService
{
    protected MomentClockSet Clocks { get; }
    protected IChatService Chats { get; }
    protected MediaPlayer MediaPlayer { get; }
    protected IChatMediaResolver MediaResolver { get; }
    protected AudioDownloader AudioDownloader { get; }
    protected AudioIndexService AudioIndex { get; }
    protected IAudioSourceStreamer AudioSourceStreamer { get; }
    protected ILogger Log { get; }

    public ChatId ChatId { get; init; }
    public Session Session { get; init; }

    private async Task WatchRealtimeMedia(CancellationToken cancellationToken)
    {
        var chatId = ChatId.Value.NullIfEmpty() ?? ChatConstants.DefaultChatId;
        var idLogCover = ChatConstants.IdTiles;
        try {
            var lastChatEntry = 0L;
            var computedMinMax = await Computed.Capture(ct => Chats.GetIdRange(Session, ChatId, ct), cancellationToken)
                .ConfigureAwait(false);
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                if (computedMinMax.IsConsistent()) {
                    var minMax = computedMinMax.Value;
                    var maxEntry = minMax.End;
                    if (lastChatEntry == 0)
                        lastChatEntry = Math.Max(0, maxEntry - 128);

                    var ranges = idLogCover.GetTileCover((lastChatEntry, maxEntry + 1));
                    var entryLists = await Task.WhenAll(
                            ranges.Select(r => Chats.GetEntries(Session, chatId, r, cancellationToken)))
                        .ConfigureAwait(false);
                    var chatEntries = entryLists.SelectMany(entries => entries);
                    foreach (var entry in chatEntries.Where(
                                 ce => ce.IsStreaming && ce.ContentType == ChatContentType.Audio)) {
                        if (lastChatEntry >= entry.Id) continue;

                        lastChatEntry = entry.Id;
                        _ = PlayStreamingMediaTrack(entry, cancellationToken);
                    }
                }
                await computedMinMax.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                computedMinMax = await computedMinMax.Update(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Error watching for new chat entries for audio");
            throw;
        }

        // ReSharper disable once FunctionNeverReturns
    }

    private async Task PlayStreamingMediaTrack(ChatEntry entry, CancellationToken cancellationToken)
    {
        try {
            if (!entry.IsStreaming) return;

            var beginsAt = entry.BeginsAt;
            var cutoffTime = Clocks.CpuClock.Now - TimeSpan.FromMinutes(1);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);
            if (beginsAt < cutoffTime) return;

            var offset = beginsAt - cutoffTime;
            var audioSource = await AudioSourceStreamer.GetAudioSource(entry.StreamId, offset, cancellationToken);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, beginsAt, cancellationToken);
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(e,
                "Error reading media stream. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}, StreamId = {StreamId}",
                entry.ChatId,
                entry.Id,
                entry.StreamId);
        }
    }

    private async Task PlayMediaTrack(ChatEntry entry, TimeSpan offset, CancellationToken cancellationToken)
    {
        try {
            var audioEntry = await AudioIndex.FindAudioEntry(Session, entry, offset, cancellationToken);
            if (audioEntry == null)
                throw new InvalidOperationException(
                    $"Unable to find audio entry for ChatEntry with ChatId = {entry.ChatId}, Id = {entry.Id}.");

            if (audioEntry.IsStreaming) {
                await PlayStreamingMediaTrack(audioEntry, cancellationToken);
                return;
            }

            var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
            var audioSource = await AudioDownloader.DownloadAsAudioSource(audioBlobUri, offset, cancellationToken);
            var trackId = ZString.Concat("audio:", entry.ChatId, entry.Id);

            await MediaPlayer.Stop();
            await MediaPlayer.AddMediaTrack(trackId, audioSource, audioEntry.BeginsAt + offset, cancellationToken);
            _ = MediaPlayer.Play();
        }
        catch (Exception e) when (e is not TaskCanceledException) {
            Log.LogError(
                e,
                "Error reading media stream. Chat: {ChatId}, Entry: {ChatEntryId}, StreamId: {StreamId}",
                entry.ChatId,
                entry.Id,
                entry.StreamId);
        }
    }
}
