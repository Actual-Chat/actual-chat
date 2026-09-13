using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Streaming.Services;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.UnitTests;

// Runs a real muxer against fake stream services, so the relay paths ProcessStream takes
// (start item, header hold, dub fallback) are exercised end to end
public class ListeningStreamMuxerRelayTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Author1 = AuthorId.New(TestChatId, 1);

    [Fact(Timeout = 30_000)]
    public async Task DubErrorShouldFallBackToTheOriginalFromTheLiveEdge()
    {
        // arrange
        var sourceId = StreamId.New(new NodeRef(Generate.Option));
        var dubId = StreamId.New(sourceId, Languages.English);
        var streamInfo = new LiveAudioStreamInfo {
            ChatId = TestChatId,
            AuthorId = Author1,
            StreamId = sourceId.Value,
            BeginsAt = new Moment(DateTime.UtcNow),
            Languages = new ApiArray<Language>([Languages.Russian]),
        };
        var streams = new FakeLiveAudioStreams {
            [dubId.Value] = _ => Frames(1, StandardError.External("TTS is down")),
            [sourceId.Value] = _ => Frames(3, null),
        };
        var services = new ServiceCollection()
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddXUnit(Out))
            .AddSingleton<ILiveAudioStreams>(streams)
            .AddSingleton(new LiveAudioStreamInfo[] { streamInfo });
        services.AddFusion().AddService<ILiveAudioBackend, FakeLiveAudioBackend>();
        await using var serviceProvider = services.BuildServiceProvider();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // act
        await using var muxer = new ListeningStreamMuxer(
            serviceProvider, Session.New(), TestChatId, default, Languages.English);
        var items = new List<MuxedAudioStreamItem>();
        await foreach (var item in muxer.Output.ReadAllAsync(cts.Token)) {
            items.Add(item);
            if (items.OfType<MuxedAudioStreamStart>().Count() == 2 && item is MuxedAudioFrame)
                break;
        }

        // assert
        var starts = items.OfType<MuxedAudioStreamStart>().ToList();
        starts[0].StreamInfo.DubLanguage.Should().Be(Languages.English, "the dub was served first");
        starts[1].StreamInfo.DubLanguage.Should().BeNull(
            "a dub that dies mid-relay must not mute the speaker: the same entry is retried with the original");
        starts[1].StreamInfo.StreamId.Should().Be(sourceId.Value);
        items.OfType<MuxedAudioStreamEnd>().Should().ContainSingle(x => x.StreamIndex == starts[0].StreamIndex,
            "the dub track is ended before the original starts");
        streams.GetSkipTo(sourceId.Value).Should().Be(Constants.Audio.SkipToLive,
            "the listener already heard the utterance's start, so the original is served live");
        streams.GetRequestCount(dubId.Value).Should().Be(1, "a failed dub isn't asked for again");
    }

    // Private methods

    private static async IAsyncEnumerable<AudioFrame> Frames(int dataFrameCount, Exception? error)
    {
        yield return new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromMilliseconds(-1) };
        for (var i = 0; i < dataFrameCount; i++) {
            await Task.Delay(10).ConfigureAwait(false);
            yield return new AudioFrame { Data = new byte[] { 2 }, Offset = Constants.Audio.OpusFrameDuration * i };
        }
        if (error != null)
            throw error;
    }

    // Nested types

    public class FakeLiveAudioBackend(LiveAudioStreamInfo[] streams) : ILiveAudioBackend
    {
        [ComputeMethod]
        public virtual Task<ApiArray<LiveAudioStreamInfo>> List(ChatId chatId, CancellationToken cancellationToken)
            => Task.FromResult(new ApiArray<LiveAudioStreamInfo>(streams));

        public Task Register(ChatId chatId, LiveAudioStreamInfo streamInfo, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task Unregister(ChatId chatId, string streamId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakeLiveAudioStreams : ILiveAudioStreams
    {
        private readonly Dictionary<string, Func<TimeSpan, IAsyncEnumerable<AudioFrame>>> _sources = new();
        private readonly ConcurrentDictionary<string, int> _requestCounts = new();
        private readonly ConcurrentDictionary<string, TimeSpan> _skipTos = new();

        public Func<TimeSpan, IAsyncEnumerable<AudioFrame>> this[string streamId] {
            set => _sources[streamId] = value;
        }

        public int GetRequestCount(string streamId)
            => _requestCounts.GetValueOrDefault(streamId);

        public TimeSpan GetSkipTo(string streamId)
            => _skipTos[streamId];

        public Task<RpcStream<AudioFrame>?> GetStream(
            Session session,
            string streamId,
            TimeSpan skipTo,
            CancellationToken cancellationToken)
        {
            _requestCounts.AddOrUpdate(streamId, 1, (_, count) => count + 1);
            _skipTos[streamId] = skipTo;
            var source = _sources.GetValueOrDefault(streamId);
            var stream = source == null ? null : StandardRpcStream.NewAudioDelivery(source.Invoke(skipTo));
            return Task.FromResult(stream);
        }

        public Task<ApiArray<LiveAudioStreamInfo>> List(
            Session session, ChatId chatId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RpcStream<TranscriptDiff>?> GetTranscriptStream(
            Session session, string streamId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
            Session session, ChatId chatId, Moment catchUpFrom, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RpcStream<MuxedAudioStreamItem>> GetListeningStream(
            Session session, ChatId chatId, Moment catchUpFrom, Language? dubLanguage,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RpcStream<MuxedAudioStreamItem>> GetReplayStream(
            Session session, ChatId chatId, Moment startAt, TimeSpan rewindOffset, double speed,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task PushStream(
            Session session, string chatId, string? repliedChatEntryId, double clientStartAt, int preSkip,
            RpcStream<AudioFrame> frameStream, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReportAudioLatency(Session session, TimeSpan latency, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReportAudioLatency(
            Session session, TimeSpan latency, TimeSpan? avSyncError, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ReportPlayback(
            Session session, ChatId chatId, string streamId, ChatEntryId? entryId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RpcStream<MuxedAudioStreamItem>> LegacyGetListeningStream(
            Session session, ChatId chatId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task LegacyChangeSettings(
            Session session, ChatId chatId, LegacyLiveStreamSettings settings, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
