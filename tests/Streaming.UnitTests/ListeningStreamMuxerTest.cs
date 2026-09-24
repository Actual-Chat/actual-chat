using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

/// <summary>
/// Tests for LiveStreamMuxer's per-author dedup via TryRegister. Exercises
/// private helpers via reflection to validate concurrency-sensitive paths
/// without needing full service wiring.
/// </summary>
public class ListeningStreamMuxerTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Author1 = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Author2 = AuthorId.New(TestChatId, 2);

    [Fact]
    public void TryRegisterFirstStreamWins()
    {
        var h = new MuxerHarness();
        var cts = new CancellationTokenSource();

        h.Register(Author1, "stream-1", Now(), cts).Should().BeTrue();
        h.GetActiveStreamId(Author1).Should().Be("stream-1");
        h.HasById("stream-1").Should().BeTrue();
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void TryRegisterFresherStreamReplacesOlder()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, cts1).Should().BeTrue();
        h.Register(Author1, "stream-2", t2, cts2).Should().BeTrue();

        h.GetActiveStreamId(Author1).Should().Be("stream-2");
        cts1.IsCancellationRequested.Should().BeTrue("old stream should be cancelled");
        cts2.IsCancellationRequested.Should().BeFalse("new stream should stay");
    }

    [Fact]
    public void TryRegisterStaleStreamLoses()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 - TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, cts1).Should().BeTrue();
        h.Register(Author1, "stream-2", t2, cts2).Should().BeFalse();

        h.GetActiveStreamId(Author1).Should().Be("stream-1");
        cts1.IsCancellationRequested.Should().BeFalse("existing fresher stream should stay");
        cts2.IsCancellationRequested.Should().BeTrue("stale newcomer should be cancelled");
    }

    [Fact]
    public void TryRegisterSameBeginsAtPrefersTheNewcomer()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t = Now();

        h.Register(Author1, "stream-1", t, cts1).Should().BeTrue();
        h.Register(Author1, "stream-2", t, cts2)
            .Should().BeTrue("a sender retrying its push within MaxBeginsAtDrift claims a start "
                + "equal to the call it replaces, and keeping the older entry keeps the dead one");

        h.GetActiveStreamId(Author1).Should().Be("stream-2");
        cts1.IsCancellationRequested.Should().BeTrue("the superseded stream should be cancelled");
        cts2.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void TryRegisterExcludesTheStreamItSupersedes()
    {
        var h = new MuxerHarness();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, new CancellationTokenSource()).Should().BeTrue();
        h.Register(Author1, "stream-2", t2, new CancellationTokenSource()).Should().BeTrue();

        h.IsExcluded("stream-1").Should().BeTrue(
            "a superseded stream the server still lists would otherwise be recreated, win the tie "
            + "back against the live one, and be served from position 0");
        h.IsExcluded("stream-2").Should().BeFalse();
    }

    [Fact]
    public void TryRegisterDisposedCtsDoesNotThrow()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        cts1.Dispose(); // simulate finished stream that already disposed its CTS

        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(1);

        h.Register(Author1, "stream-1", t1, cts1);

        var act = () => h.Register(Author1, "stream-2", t2, cts2);
        act.Should().NotThrow("cancel on a disposed CTS should be swallowed");
        h.GetActiveStreamId(Author1).Should().Be("stream-2");
    }

    [Fact]
    public void TryRegisterDifferentAuthorsAreIndependent()
    {
        var h = new MuxerHarness();
        var t = Now();

        h.Register(Author1, "stream-a", t, new CancellationTokenSource()).Should().BeTrue();
        h.Register(Author2, "stream-b", t, new CancellationTokenSource()).Should().BeTrue();

        h.GetActiveStreamId(Author1).Should().Be("stream-a");
        h.GetActiveStreamId(Author2).Should().Be("stream-b");
    }

    [Fact]
    public void GetSkipToRequestsLiveEdgeForAPreexistingStream()
    {
        // arrange
        var streamInfo = StreamInfo(Author1, "stream-1", Now());

        // act
        var skipTo = ListeningStreamMuxer.GetSkipTo(true, streamInfo, default);

        // assert
        skipTo.Should().Be(Constants.Audio.SkipToLive);
    }

    [Fact]
    public void GetSkipToKeepsAStreamThatStartedWhileWatching()
    {
        // arrange
        var streamInfo = StreamInfo(Author1, "stream-1", Now());

        // act
        var skipTo = ListeningStreamMuxer.GetSkipTo(false, streamInfo, default);

        // assert
        skipTo.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetSkipToServesACatchUpTargetFromItsStart()
    {
        // arrange
        var beginsAt = Now();
        var streamInfo = StreamInfo(Author1, "stream-1", beginsAt);

        // act
        var skipTo = ListeningStreamMuxer.GetSkipTo(true, streamInfo, beginsAt);

        // assert
        skipTo.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void MustSkipSynthesizedShouldFollowTheListenersChoice()
    {
        // The listener decides what may be synthesized for them. Skipping is also what makes it
        // free: nothing asks for the stream, so nothing is ever synthesized on their behalf.

        // arrange
        var spoken = new LiveAudioStreamInfo { IsSynthesized = true };
        var recorded = new LiveAudioStreamInfo();

        // act, assert
        ListeningStreamMuxer.MustSkipSynthesized(spoken, true).Should().BeFalse();
        ListeningStreamMuxer.MustSkipSynthesized(spoken, false).Should().BeTrue();
        ListeningStreamMuxer.MustSkipSynthesized(recorded, false).Should().BeFalse(
            "a real voice is heard whatever the listener thinks of synthesized text");
    }

    [Fact]
    public void MustDubShouldRequireASpeakerWhoDoesNotSpeakTheListenersLanguage()
    {
        // arrange
        var russianSpeaker = StreamInfo(Author1, "s", Now()) with {
            Languages = new ApiArray<Language>([Languages.Russian]),
        };
        var bilingual = StreamInfo(Author1, "s", Now()) with {
            Languages = new ApiArray<Language>([Languages.Russian, Language.Parse("en-US")]),
        };
        var unknown = StreamInfo(Author1, "s", Now());

        // act
        var russianForEnglish = ListeningStreamMuxer.MustDub(russianSpeaker, Languages.English);
        var russianForRussian = ListeningStreamMuxer.MustDub(russianSpeaker, Languages.Russian);
        var bilingualForBritish = ListeningStreamMuxer.MustDub(bilingual, Language.Parse("en-GB"));
        var russianForNone = ListeningStreamMuxer.MustDub(russianSpeaker, null);
        var unknownForEnglish = ListeningStreamMuxer.MustDub(unknown, Languages.English);

        // assert
        russianForEnglish.Should().BeTrue();
        russianForRussian.Should().BeFalse();
        bilingualForBritish.Should().BeFalse("English variants match");
        russianForNone.Should().BeFalse("no dub language requested");
        unknownForEnglish.Should().BeFalse(
            "a stream from an older server carries no languages, and guessing would hold audio for nothing");
    }

    [Fact]
    public void TryRegisterShouldNotMergeDubbedStreamsOfTheSameAuthor()
    {
        // arrange
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(5);

        // act
        var isFirstRegistered = h.Register(Author1, "stream-1", t1, cts1, isDubbed: true);
        var isSecondRegistered = h.Register(Author1, "stream-2", t2, cts2, isDubbed: true);

        // assert
        isFirstRegistered.Should().BeTrue();
        isSecondRegistered.Should().BeTrue();
        cts1.IsCancellationRequested.Should().BeFalse(
            "a dub outlives its source by the translation lag plus the spoken length, so the next "
            + "utterance must not cut it; the backend serializes dubs per author instead");
        h.HasById("stream-1").Should().BeTrue();
        h.HasById("stream-2").Should().BeTrue();
        h.GetActiveStreamId(Author1).Should().BeNull("dubbed entries stay out of the per-author map");
    }

    [Fact]
    public void TryRegisterShouldMergeAFallenBackOriginal()
    {
        // arrange
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = Now();
        var t2 = t1 + TimeSpan.FromSeconds(5);

        // act
        var isFirstRegistered = h.Register(Author1, "stream-1", t1, cts1, isDubbed: true, isFallenBack: true);
        var isSecondRegistered = h.Register(Author1, "stream-2", t2, cts2, isDubbed: true, isFallenBack: true);

        // assert
        isFirstRegistered.Should().BeTrue();
        isSecondRegistered.Should().BeTrue();
        cts1.IsCancellationRequested.Should().BeTrue(
            "originals evict originals even for a dubbing listener: a dub that fell back to the source "
            + "is an original, and a recorder reconnect must not leave two of them overlapping");
        cts2.IsCancellationRequested.Should().BeFalse();
        h.GetActiveStreamId(Author1).Should().Be("stream-2");
        h.IsExcluded("stream-1").Should().BeTrue();
    }

    [Fact]
    public void StampDubStartShouldPutTheTimelineOriginAtTheFirstFrame()
    {
        // arrange
        var streamInfo = StreamInfo(Author1, "stream-1", Now() - TimeSpan.FromMinutes(1));
        var now = Now();
        var firstFrame = new AudioFrame { Offset = TimeSpan.FromSeconds(12) };

        // act
        var stamped = ListeningStreamMuxer.StampDubStart(streamInfo, firstFrame, now);

        // assert
        stamped.BeginsAt.Should().Be(now - TimeSpan.FromSeconds(12),
            "a listener joining mid-dub gets a frame at a non-zero offset, and BeginsAt + Offset must be now");
        stamped.SourceBeginsAt.Should().Be(stamped.BeginsAt);
    }

    private static Moment Now() => new(DateTime.UtcNow);

    private static LiveAudioStreamInfo StreamInfo(AuthorId authorId, string streamId, Moment beginsAt)
        => new() {
            ChatId = TestChatId,
            AuthorId = authorId,
            StreamId = streamId,
            BeginsAt = beginsAt,
        };

    // Test harness

    /// <summary>
    /// Creates a LiveStreamMuxer without running the worker and exposes
    /// its private TryRegister and internal dicts for isolated testing.
    /// </summary>
    private sealed class MuxerHarness
    {
        private static readonly Type MuxerType = typeof(ListeningStreamMuxer);
        private static readonly Type StreamEntryType =
            MuxerType.GetNestedType("StreamEntry", BindingFlags.NonPublic)!;
        private static readonly FieldInfo StreamByIdField =
            MuxerType.GetField("_streamById", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo StreamByAuthorField =
            MuxerType.GetField("_streamByAuthor", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo TryRegisterMethod =
            MuxerType.GetMethod("TryRegister", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly PropertyInfo StreamIdProp = StreamEntryType.GetProperty("StreamId")!;

        private readonly ListeningStreamMuxer _muxer;
        private readonly object _streamByAuthor;
        private readonly object _streamById;

        public MuxerHarness()
        {
            var services = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Debug))
                .BuildServiceProvider();

            // Allocate without running constructor (which starts the worker)
            _muxer = (ListeningStreamMuxer)RuntimeHelpers.GetUninitializedObject(MuxerType);

            // Every dictionary, not just the two TryRegister happened to touch when this harness
            // was written: GetUninitializedObject leaves them all null, so one that TryRegister
            // starts using later fails here as a NullReferenceException rather than as a
            // recognisable gap. Initialising them by shape keeps that from recurring.
            foreach (var field in MuxerType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)) {
                if (field.GetValue(_muxer) is not null)
                    continue;
                if (field.FieldType is { IsGenericType: true } type
                    && type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                    field.SetValue(_muxer, Activator.CreateInstance(field.FieldType));
            }
            _streamById = StreamByIdField.GetValue(_muxer)!;
            _streamByAuthor = StreamByAuthorField.GetValue(_muxer)!;

            // Initialize backing fields for DI properties used by TryRegister
            var servicesBackingField = MuxerType.GetField("<Services>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            servicesBackingField.SetValue(_muxer, services);

            var logBackingField = MuxerType.GetField("<Log>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (logBackingField != null)
                logBackingField.SetValue(_muxer, services.GetRequiredService<ILoggerFactory>().CreateLogger<ListeningStreamMuxer>());
        }

        // Mirrors ProcessStream: registered by id before GetStream, merged per author after it, and
        // the merge is skipped only when the served stream is actually a dub (isFallenBack = false)
        public bool Register(
            AuthorId authorId, string streamId, Moment beginsAt, CancellationTokenSource cts,
            bool isDubbed = false, bool isFallenBack = false)
        {
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = TestChatId,
                AuthorId = authorId,
                StreamId = streamId,
                BeginsAt = beginsAt,
            };
            var entry = Activator.CreateInstance(StreamEntryType, 0, streamInfo, cts)!;
            StreamEntryType.GetProperty("IsDubbed")!.SetValue(entry, isDubbed);
            _streamById.GetType().GetMethod("TryAdd")!.Invoke(_streamById, [streamId, entry]);
            var isDub = isDubbed && !isFallenBack;
            return (bool)TryRegisterMethod.Invoke(_muxer, [entry, isDub])!;
        }

        public string? GetActiveStreamId(AuthorId authorId)
        {
            var tryGetValue = _streamByAuthor.GetType().GetMethod("TryGetValue")!;
            var args = new object?[] { authorId, null };
            if (!(bool)tryGetValue.Invoke(_streamByAuthor, args)!)
                return null;
            return (string)StreamIdProp.GetValue(args[1]!)!;
        }

        public bool IsExcluded(string streamId)
        {
            var excluded = MuxerType
                .GetField("_excludedStreamIds", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_muxer)!;
            return (bool)excluded.GetType().GetMethod("ContainsKey")!.Invoke(excluded, [streamId])!;
        }

        public bool HasById(string streamId)
        {
            var containsKey = _streamById.GetType().GetMethod("ContainsKey")!;
            return (bool)containsKey.Invoke(_streamById, [streamId])!;
        }
    }
}
