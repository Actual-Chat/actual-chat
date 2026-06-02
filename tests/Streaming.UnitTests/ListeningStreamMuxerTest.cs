using System.Reflection;
using System.Runtime.CompilerServices;
using ActualChat.Chat;
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
    public void TryRegisterSameBeginsAtKeepsExisting()
    {
        var h = new MuxerHarness();
        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t = Now();

        h.Register(Author1, "stream-1", t, cts1).Should().BeTrue();
        h.Register(Author1, "stream-2", t, cts2).Should().BeFalse("equal BeginsAt uses strict >, existing keeps");

        h.GetActiveStreamId(Author1).Should().Be("stream-1");
        cts1.IsCancellationRequested.Should().BeFalse();
        cts2.IsCancellationRequested.Should().BeTrue();
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

    private static Moment Now() => new(DateTime.UtcNow);

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

            StreamByIdField.SetValue(_muxer, Activator.CreateInstance(StreamByIdField.FieldType));
            StreamByAuthorField.SetValue(_muxer, Activator.CreateInstance(StreamByAuthorField.FieldType));
            _streamById = StreamByIdField.GetValue(_muxer)!;
            _streamByAuthor = StreamByAuthorField.GetValue(_muxer)!;

            // Initialize backing fields for DI properties used by TryRegister
            var servicesBackingField = MuxerType.GetField("<Services>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            servicesBackingField.SetValue(_muxer, services);

            var logBackingField = MuxerType.GetField("<Log>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (logBackingField != null)
                logBackingField.SetValue(_muxer, services.GetRequiredService<ILoggerFactory>().CreateLogger<ListeningStreamMuxer>());
        }

        public bool Register(AuthorId authorId, string streamId, Moment beginsAt, CancellationTokenSource cts)
        {
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = TestChatId,
                AuthorId = authorId,
                StreamId = streamId,
                BeginsAt = beginsAt,
            };
            var entry = Activator.CreateInstance(StreamEntryType, 0, streamInfo, cts)!;
            return (bool)TryRegisterMethod.Invoke(_muxer, [entry])!;
        }

        public string? GetActiveStreamId(AuthorId authorId)
        {
            var tryGetValue = _streamByAuthor.GetType().GetMethod("TryGetValue")!;
            var args = new object?[] { authorId, null };
            if (!(bool)tryGetValue.Invoke(_streamByAuthor, args)!)
                return null;
            return (string)StreamIdProp.GetValue(args[1]!)!;
        }

        public bool HasById(string streamId)
        {
            var containsKey = _streamById.GetType().GetMethod("ContainsKey")!;
            return (bool)containsKey.Invoke(_streamById, [streamId])!;
        }
    }
}
