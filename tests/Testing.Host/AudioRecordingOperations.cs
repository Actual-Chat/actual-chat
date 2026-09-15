using ActualChat.Audio;
using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Testing.Host;

public static class AudioRecordingOperations
{
    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromMilliseconds(20);

    public static async Task<ChatEntry> RecordVoiceEntry(
        this IWebTester tester,
        ChatId chatId,
        Language language,
        VoiceMode voiceMode = VoiceMode.TextAndVoice,
        int frameCount = 200,
        CancellationToken cancellationToken = default)
    {
        var services = tester.AppServices;
        var session = tester.Session;
        var userSettingsUI = services.UserSettingsUI(session);

        await userSettingsUI.UserLanguageSettings()
            .Set(new UserLanguageSettings { Primary = language }, cancellationToken)
            .ConfigureAwait(false);
        await userSettingsUI.ChatUserSettings(chatId)
            .Update(x => x with { Language = language, VoiceMode = voiceMode }, cancellationToken)
            .ConfigureAwait(false);

        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var thisNode = services.MeshWatcher().ThisNode;
        var streamId = StreamId.New(thisNode.Ref);
        var audioRecord = new AudioRecord(
            streamId,
            session,
            chatId,
            services.Clocks().SystemClock.Now.EpochOffset.TotalSeconds,
            null);

        var lidRangeBefore = await services.GetRequiredService<IChatsBackend>()
            .GetLidRange(chatId, true, cancellationToken).ConfigureAwait(false);

        var frames = GenerateAudioFrames(frameCount, services);
        await backend.ProcessAudio(audioRecord, 0,
                new RpcStream<AudioFrame>(frames),
                cancellationToken)
            .ConfigureAwait(false);

        return await WaitForNextEntry(tester, chatId, lidRangeBefore.End, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<MediaId> OptInOwnVoice(
        this IWebTester tester,
        ChatId chatId,
        Language language,
        int frameCount = 600,
        CancellationToken cancellationToken = default)
    {
        // Records a sample recording and opts the signed-in user into their own voice, so their
        // next dub is eligible for a VoicePool clone
        var entry = await tester
            .RecordVoiceEntry(chatId, language, frameCount: frameCount, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var mediaId = entry.Audio!.MediaId!;
        await tester.AppServices.UserSettingsUI(tester.Session)
            .UserLanguageSettings()
            .Update(x => x with { IsOwnVoiceEnabled = true, OwnVoiceSampleMediaId = mediaId }, cancellationToken)
            .ConfigureAwait(false);
        return mediaId;
    }

    // Private methods

    private static async IAsyncEnumerable<AudioFrame> GenerateAudioFrames(int frameCount, IServiceProvider services)
    {
        // Real Opus frames of a quiet tone, so whatever decodes the stored recording gets the
        // audio it expects - a voice sample, for one - rather than packets libopus rejects
        var log = services.LogFor(typeof(AudioRecordingOperations));
        var audio = SpeechSynthesizerExt.ToAudioSource(ProduceTone, services.Clocks(), log, CancellationToken.None);
        await foreach (var frame in audio.GetFrames(CancellationToken.None).ConfigureAwait(false)) {
            yield return frame with { Duration = DefaultFrameDuration };
            await Task.Delay(5).ConfigureAwait(false);
        }
        yield break;

        async Task ProduceTone(ChannelWriter<byte[]> pcm, CancellationToken cancellationToken)
        {
            var pcmFrame = new byte[OpusFramePump.FrameByteLength];
            for (var i = 0; i < frameCount; i++) {
                for (var j = 0; j < OpusFramePump.FrameLength; j++) {
                    var time = (double)(i * OpusFramePump.FrameLength + j) / OpusFramePump.SampleRate;
                    var sample = (short)(2000 * Math.Sin(2 * Math.PI * 440 * time));
                    BitConverter.TryWriteBytes(pcmFrame.AsSpan(j * sizeof(short)), sample);
                }
                await pcm.WriteAsync(pcmFrame.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            pcm.Complete();
        }
    }

    private static async Task<ChatEntry> WaitForNextEntry(
        IWebTester tester,
        ChatId chatId,
        long minLid,
        CancellationToken cancellationToken)
    {
        var chatsBackend = tester.AppServices.GetRequiredService<IChatsBackend>();
        ChatEntry? found = null;
        await TestExt.When(async () => {
            var range = await chatsBackend.GetLidRange(chatId, true, cancellationToken).ConfigureAwait(false);
            range.End.Should().BeGreaterThan(minLid);
            var idTile = Constants.Chat.EntryIdTiles.GetTile(range.End - 1);
            var tile = await chatsBackend.GetTile(chatId, idTile.Range, true, cancellationToken).ConfigureAwait(false);
            var entry = tile.Entries.LastOrDefault(e => e.LocalId >= minLid && !e.IsContentStreaming);
            entry.Should().NotBeNull();
            found = entry;
        }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return found!;
    }
}
