using ActualChat.Audio;
using ActualChat.Streaming;
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

        var frames = GenerateAudioFrames(frameCount);
        await backend.ProcessAudio(audioRecord, 0,
                new RpcStream<AudioFrame>(frames),
                cancellationToken)
            .ConfigureAwait(false);

        return await WaitForNextEntry(tester, chatId, lidRangeBefore.End, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static async IAsyncEnumerable<AudioFrame> GenerateAudioFrames(int frameCount)
    {
        var offset = TimeSpan.Zero;
        for (var i = 0; i < frameCount; i++) {
            var data = new byte[100];
            Array.Fill(data, (byte)(i % 256));
            yield return new AudioFrame {
                Data = data,
                Offset = offset,
                Duration = DefaultFrameDuration,
            };
            offset += DefaultFrameDuration;
            await Task.Delay(5).ConfigureAwait(false);
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
            var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(range.End - 1);
            var tile = await chatsBackend.GetTile(chatId, idTile.Range, true, cancellationToken).ConfigureAwait(false);
            var entry = tile.Entries.LastOrDefault(e => e.LocalId >= minLid && !e.IsContentStreaming);
            entry.Should().NotBeNull();
            found = entry;
        }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return found!;
    }
}
