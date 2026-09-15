using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Hashing;
using ActualChat.IO;
using ActualChat.Media;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

public sealed record VoiceSample(HashString Hash, string BlobId, TimeSpan Duration);

public enum VoiceSampleFailure
{
    None = 0,
    NotEnoughRecordings,
    SampleMissing,
}

/// <summary>
/// Builds the reference clip a voice clone is made from: the explicit sample when the user recorded
/// one, otherwise the longest of their recent recordings joined into one WAV. The clip is keyed by
/// a hash of what went in, so an unchanged selection reuses the stored blob.
/// </summary>
public sealed class VoiceSampleBuilder(IServiceProvider services)
{
    private const int SampleRate = Constants.Audio.RecordingSampleRate;
    private const int BytesPerSecond = SampleRate * Constants.Audio.Channels * sizeof(short);
    private static readonly int MaxPcmLength = PcmLengthOf(Constants.Audio.VoiceSampleMaxDuration);
    private static readonly int MinPcmLength = PcmLengthOf(Constants.Audio.VoiceSampleMinDuration);
    private static readonly TileLayer<long> EntryIdTiles = Constants.Chat.EntryIdTiles;

    private IServiceProvider Services { get; } = services;
    private IChatUsagesBackend ChatUsagesBackend => field ??= Services.GetRequiredService<IChatUsagesBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IMediaBackend MediaBackend => field ??= Services.GetRequiredService<IMediaBackend>();
    private AudioSourceDownloader AudioDownloader => field ??= Services.GetRequiredService<AudioSourceDownloader>();
    private IBlobStorages Blobs => field ??= Services.GetRequiredService<IBlobStorages>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<(VoiceSample? Sample, VoiceSampleFailure Failure)> Build(
        UserId userId,
        UserLanguageSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.OwnVoiceSampleMediaId is { } mediaId)
            return await BuildExplicit(userId, mediaId, cancellationToken).ConfigureAwait(false);

        return await BuildAuto(userId, cancellationToken).ConfigureAwait(false);
    }

    public static string BlobIdOf(UserId userId, HashString hash)
        => BlobPath.Format(BlobScope.AudioRecord, "voice-sample", userId.Value, ShortHashOf(hash) + ".wav");

    // Names the blob and the Soniox voice, so it must stay file- and identifier-safe
    public static string ShortHashOf(HashString hash)
        => HashOutputExt.FromBase64(hash.Hash).AlphaNumeric(6);

    // Internal for tests

    internal static IReadOnlyList<ChatEntry> SelectEntries(IEnumerable<ChatEntry> ownAudioEntries, Moment now)
    {
        var windowStart = now - Constants.Audio.VoiceSampleWindow;
        var candidates = ownAudioEntries
            .Where(x => !x.IsRemoved
                && x.BeginsAt >= windowStart
                && x.Audio is { IsStreaming: false } audio
                && !audio.BlobId.IsNullOrEmpty()
                && AudioDurationOf(x) >= Constants.Audio.VoiceSampleMinEntryDuration)
            .OrderByDescending(AudioDurationOf)
            .ThenBy(x => x.Id.Value);
        var selected = new List<ChatEntry>();
        var total = TimeSpan.Zero;
        foreach (var entry in candidates) {
            if (total >= Constants.Audio.VoiceSampleMaxDuration)
                break;

            selected.Add(entry);
            total += AudioDurationOf(entry);
        }
        return selected;
    }

    internal static TimeSpan TotalDuration(IReadOnlyList<ChatEntry> entries)
    {
        var total = entries.Aggregate(TimeSpan.Zero, (sum, x) => sum + AudioDurationOf(x));
        return TimeSpanExt.Min(total, Constants.Audio.VoiceSampleMaxDuration);
    }

    internal static HashString HashOf(IEnumerable<ChatEntryId> ids)
        => HashOf(string.Join('\n', ids.Select(x => x.Value)));

    internal static HashString HashOf(MediaId mediaId)
        => HashOf(mediaId.Value);

    // Private methods

    private async Task<(VoiceSample?, VoiceSampleFailure)> BuildExplicit(
        UserId userId,
        MediaId mediaId,
        CancellationToken cancellationToken)
    {
        var media = await MediaBackend.Get(mediaId, cancellationToken).ConfigureAwait(false);
        if (media == null || media.BlobId.IsNullOrEmpty())
            return (null, VoiceSampleFailure.SampleMissing);

        var hash = HashOf(mediaId);
        var stored = await GetStored(userId, hash, cancellationToken).ConfigureAwait(false);
        if (stored != null)
            return (stored, VoiceSampleFailure.None);

        var pcm = await Decode([media.BlobId], cancellationToken).ConfigureAwait(false);
        if (pcm.Length == 0)
            return (null, VoiceSampleFailure.SampleMissing);

        var sample = await Store(userId, hash, pcm, cancellationToken).ConfigureAwait(false);
        return (sample, VoiceSampleFailure.None);
    }

    private async Task<(VoiceSample?, VoiceSampleFailure)> BuildAuto(UserId userId, CancellationToken cancellationToken)
    {
        var entries = await ListOwnEntries(userId, cancellationToken).ConfigureAwait(false);
        var selected = SelectEntries(entries, Clocks.SystemClock.Now);
        if (TotalDuration(selected) < Constants.Audio.VoiceSampleMinDuration)
            return (null, VoiceSampleFailure.NotEnoughRecordings);

        var hash = HashOf(selected.Select(x => x.Id));
        var stored = await GetStored(userId, hash, cancellationToken).ConfigureAwait(false);
        if (stored != null)
            return (stored, VoiceSampleFailure.None);

        var blobIds = selected.Select(x => x.Audio!.BlobId);
        var pcm = await Decode(blobIds, cancellationToken).ConfigureAwait(false);
        if (pcm.Length < MinPcmLength) {
            // The entries promised enough, their blobs delivered less - most likely some are gone
            Log.LogWarning("Build: {UserId}'s recordings decoded to {Duration} of speech, not enough for a sample",
                userId, DurationOf(pcm.Length));
            return (null, VoiceSampleFailure.NotEnoughRecordings);
        }

        var sample = await Store(userId, hash, pcm, cancellationToken).ConfigureAwait(false);
        return (sample, VoiceSampleFailure.None);
    }

    private async Task<List<ChatEntry>> ListOwnEntries(UserId userId, CancellationToken cancellationToken)
    {
        var entries = new List<ChatEntry>();
        var windowStart = Clocks.SystemClock.Now - Constants.Audio.VoiceSampleWindow;
        foreach (var chatId in await ListRecentChatIds(userId, cancellationToken).ConfigureAwait(false)) {
            var author = await AuthorsBackend
                .GetByUserId(chatId, userId, RequestedAuthorKind.Default, cancellationToken)
                .ConfigureAwait(false);
            if (author == null)
                continue;

            var recentEntries = ReadRecentEntries(chatId, windowStart, cancellationToken);
            await foreach (var entry in recentEntries.ConfigureAwait(false))
                if (entry.AuthorId == author.Id && entry.HasAudio)
                    entries.Add(entry);
        }
        return entries;
    }

    private async Task<ChatId[]> ListRecentChatIds(UserId userId, CancellationToken cancellationToken)
    {
        // A peer chat lands on the list on the first entry written there, a group chat when it's
        // viewed - together that's every chat the user has recently spoken in
        var peerChatIds = await ChatUsagesBackend
            .GetRecencyList(userId, ChatUsageListKind.PeerChatsWroteTo, cancellationToken)
            .ConfigureAwait(false);
        var groupChatIds = await ChatUsagesBackend
            .GetRecencyList(userId, ChatUsageListKind.ViewedGroupChats, cancellationToken)
            .ConfigureAwait(false);
        return peerChatIds.Take(Constants.Audio.VoiceSampleMaxChats)
            .Concat(groupChatIds.Take(Constants.Audio.VoiceSampleMaxChats))
            .Distinct()
            .ToArray();
    }

    private async IAsyncEnumerable<ChatEntry> ReadRecentEntries(
        ChatId chatId,
        Moment windowStart,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lidRange = await ChatsBackend.GetLidRange(chatId, false, cancellationToken).ConfigureAwait(false);
        if (lidRange.IsEmptyOrNegative)
            yield break;

        var entryCount = 0;
        for (var idTile = EntryIdTiles.GetTile(lidRange.End - 1); idTile.End > lidRange.Start; idTile = idTile.Prev()) {
            var tile = await ChatsBackend
                .GetTileNonComputed(chatId, idTile.Range, false, cancellationToken)
                .ConfigureAwait(false);
            if (tile.IsEmpty)
                continue;
            // Lids grow with time, so once a whole tile predates the window, everything below does too
            if (tile.BeginsAtRange.End <= windowStart)
                yield break;

            foreach (var entry in tile.Entries)
                yield return entry;

            entryCount += tile.Entries.Length;
            if (entryCount >= Constants.Audio.VoiceSampleMaxEntriesPerChat)
                yield break;
        }
    }

    private async Task<VoiceSample?> GetStored(UserId userId, HashString hash, CancellationToken cancellationToken)
    {
        var blobId = BlobIdOf(userId, hash);
        var stream = await Blobs[BlobScope.AudioRecord].Read(blobId, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        await using var _ = stream.ConfigureAwait(false);
        var header = new byte[WavWriter.HeaderLength];
        var readLength = await stream
            .ReadAtLeastAsync(header, header.Length, false, cancellationToken)
            .ConfigureAwait(false);
        var pcmLength = WavWriter.GetPcmLength(header.AsSpan(0, readLength));
        return pcmLength < 0 ? null : new VoiceSample(hash, blobId, DurationOf(pcmLength));
    }

    private async Task<VoiceSample> Store(
        UserId userId,
        HashString hash,
        byte[] pcm,
        CancellationToken cancellationToken)
    {
        var blobId = BlobIdOf(userId, hash);
        var stream = MemoryStreamManager.Default.GetStream();
        await using var _ = stream.ConfigureAwait(false);
        WavWriter.Write(stream, pcm, SampleRate);
        stream.Position = 0;
        await Blobs[BlobScope.AudioRecord].Write(blobId, stream, "audio/wav", cancellationToken).ConfigureAwait(false);
        return new VoiceSample(hash, blobId, DurationOf(pcm.Length));
    }

    private async Task<byte[]> Decode(IEnumerable<string> blobIds, CancellationToken cancellationToken)
    {
        using var decoder = new OpusToPcmDecoder();
        var pcm = new MemoryStream();
        foreach (var blobId in blobIds) {
            if (pcm.Length >= MaxPcmLength)
                break;

            // Cancelling the download once enough is decoded stops the rest of the blob from being read
            using var blobCts = cancellationToken.CreateLinkedTokenSource();
            var audio = await AudioDownloader.TryDownload(blobId, TimeSpan.Zero, blobCts.Token).ConfigureAwait(false);
            if (audio == null)
                continue;

            await foreach (var frame in audio.GetFrames(blobCts.Token).ConfigureAwait(false)) {
                var chunk = decoder.Decode(frame.Data.Span);
                pcm.Write(chunk, 0, (int)Math.Min(chunk.Length, MaxPcmLength - pcm.Length));
                if (pcm.Length >= MaxPcmLength)
                    break;
            }
            blobCts.Cancel();
        }
        return pcm.ToArray();
    }

    private static TimeSpan AudioDurationOf(ChatEntry entry)
        => TimeSpan.FromSeconds(entry.Audio?.Duration ?? entry.Duration ?? 0);

    private static TimeSpan DurationOf(long pcmLength)
        => TimeSpan.FromSeconds((double)pcmLength / BytesPerSecond);

    private static int PcmLengthOf(TimeSpan duration)
        => (int)(duration.TotalSeconds * BytesPerSecond);

    private static HashString HashOf(string input)
        => input.Hash().Blake3().ToBlake3Base64HashString();
}
