using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Streaming.Services;

namespace ActualChat.Streaming.UnitTests;

/// <summary>
/// Tests for LiveStreamMuxer's per-author stream tracking, overlap filtering,
/// and cleanup logic. Exercises private helpers via reflection to validate
/// concurrency-sensitive paths without needing full service wiring.
/// </summary>
public class LiveStreamMuxerTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly AuthorId Author1 = AuthorId.New(TestChatId, 1);
    private static readonly AuthorId Author2 = AuthorId.New(TestChatId, 2);

    // -- RegisterAuthorStream --

    [Fact]
    public void Register_FresherStreamReplacesOlder()
    {
        var h = new MuxerHarness();

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = new Moment(DateTime.UtcNow);
        var t2 = t1 + TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, cts1);
        h.Register(Author1, "stream-2", t2, cts2);

        h.GetActiveStreamId(Author1).Should().Be("stream-2");
        cts1.IsCancellationRequested.Should().BeTrue("old stream should be cancelled");
        cts2.IsCancellationRequested.Should().BeFalse("new stream should stay");
    }

    [Fact]
    public void Register_StaleStreamCancelsItself()
    {
        var h = new MuxerHarness();

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t1 = new Moment(DateTime.UtcNow);
        var t2 = t1 - TimeSpan.FromSeconds(5);

        h.Register(Author1, "stream-1", t1, cts1);
        h.Register(Author1, "stream-2", t2, cts2);

        h.GetActiveStreamId(Author1).Should().Be("stream-1");
        cts1.IsCancellationRequested.Should().BeFalse("existing fresher stream should stay");
        cts2.IsCancellationRequested.Should().BeTrue("stale newcomer should be cancelled");
    }

    [Fact]
    public void Register_DisposedCtsDoesNotThrow()
    {
        var h = new MuxerHarness();

        var cts1 = new CancellationTokenSource();
        cts1.Dispose(); // Simulate finished stream that already disposed its CTS

        var cts2 = new CancellationTokenSource();
        var t1 = new Moment(DateTime.UtcNow);
        var t2 = t1 + TimeSpan.FromSeconds(1);

        h.Register(Author1, "stream-1", t1, cts1);

        // Replacing disposed CTS must not throw ObjectDisposedException
        var act = () => h.Register(Author1, "stream-2", t2, cts2);
        act.Should().NotThrow("Cancel on disposed CTS should be caught");
        h.GetActiveStreamId(Author1).Should().Be("stream-2");
    }

    [Fact]
    public void Register_SameBeginsAtReplacesExisting()
    {
        var h = new MuxerHarness();

        var cts1 = new CancellationTokenSource();
        var cts2 = new CancellationTokenSource();
        var t = new Moment(DateTime.UtcNow);

        h.Register(Author1, "stream-1", t, cts1);
        h.Register(Author1, "stream-2", t, cts2); // same beginsAt

        h.GetActiveStreamId(Author1).Should().Be("stream-2", "equal beginsAt uses >=, so newcomer wins");
        cts1.IsCancellationRequested.Should().BeTrue();
    }

    // -- ShouldEmitFrame --

    [Fact]
    public void ShouldEmit_ActiveStreamEmits()
    {
        var h = new MuxerHarness();
        h.Register(Author1, "stream-1", new Moment(DateTime.UtcNow), new CancellationTokenSource());

        var frame = new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromSeconds(1) };
        h.ShouldEmitFrame(Author1, "stream-1", frame).Should().BeTrue();
    }

    [Fact]
    public void ShouldEmit_NoTrackingEntryEmits()
    {
        var h = new MuxerHarness();

        var frame = new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromSeconds(1) };
        h.ShouldEmitFrame(Author1, "unknown", frame).Should().BeTrue();
    }

    [Fact]
    public void ShouldEmit_SupersededStreamOverlapDropped()
    {
        var h = new MuxerHarness();
        var t1 = new Moment(DateTime.UtcNow);

        h.Register(Author1, "stream-1", t1, new CancellationTokenSource());

        // stream-2 replaces stream-1
        h.Register(Author1, "stream-2", t1 + TimeSpan.FromSeconds(1), new CancellationTokenSource());

        // New stream emits frames and advances its LastEmittedOffset
        var frame5 = new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromSeconds(5) };
        h.ShouldEmitFrame(Author1, "stream-2", frame5).Should().BeTrue();
        h.UpdateLastEmittedOffset(Author1, "stream-2", TimeSpan.FromSeconds(5));

        // Old stream's frame at 3s (within new stream's already-emitted range) — dropped
        var frame3 = new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromSeconds(3) };
        h.ShouldEmitFrame(Author1, "stream-1", frame3).Should().BeFalse();
    }

    [Fact]
    public void ShouldEmit_SupersededStreamBeyondLastEmittedStillEmits()
    {
        var h = new MuxerHarness();
        var t1 = new Moment(DateTime.UtcNow);

        h.Register(Author1, "stream-1", t1, new CancellationTokenSource());

        h.Register(Author1, "stream-2", t1 + TimeSpan.FromSeconds(1), new CancellationTokenSource());

        // New stream emits up to 5s
        h.UpdateLastEmittedOffset(Author1, "stream-2", TimeSpan.FromSeconds(5));

        // Old stream frame at 6s (beyond new stream's last emitted) — allowed
        var frame6 = new AudioFrame { Data = new byte[] { 1 }, Offset = TimeSpan.FromSeconds(6) };
        h.ShouldEmitFrame(Author1, "stream-1", frame6).Should().BeTrue();
    }

    // -- Cleanup (finally block logic) --

    [Fact]
    public void Cleanup_RemovesOwnEntry()
    {
        var h = new MuxerHarness();
        h.Register(Author1, "stream-1", new Moment(DateTime.UtcNow), new CancellationTokenSource());

        h.SimulateFinally(Author1, "stream-1");

        h.HasAuthorEntry(Author1).Should().BeFalse("should remove own entry");
    }

    [Fact]
    public void Cleanup_DoesNotRemoveNewerEntry()
    {
        var h = new MuxerHarness();
        var t1 = new Moment(DateTime.UtcNow);
        h.Register(Author1, "stream-1", t1, new CancellationTokenSource());
        h.Register(Author1, "stream-2", t1 + TimeSpan.FromSeconds(1), new CancellationTokenSource());

        // stream-1 finishes — must NOT remove stream-2's entry
        h.SimulateFinally(Author1, "stream-1");

        h.HasAuthorEntry(Author1).Should().BeTrue("newer stream's entry must survive");
        h.GetActiveStreamId(Author1).Should().Be("stream-2");
    }

    // -- Multi-author independence --

    [Fact]
    public void DifferentAuthorsAreIndependent()
    {
        var h = new MuxerHarness();
        var t = new Moment(DateTime.UtcNow);

        h.Register(Author1, "stream-a", t, new CancellationTokenSource());
        h.Register(Author2, "stream-b", t, new CancellationTokenSource());

        h.GetActiveStreamId(Author1).Should().Be("stream-a");
        h.GetActiveStreamId(Author2).Should().Be("stream-b");
    }

    // -- Test harness --

    /// <summary>
    /// Creates a LiveStreamMuxer without running the worker, and exposes
    /// its private methods for isolated testing.
    /// </summary>
    private sealed class MuxerHarness
    {
        private static readonly Type MuxerType = typeof(LiveStreamMuxer);
        private static readonly Type StateType = MuxerType.GetNestedType("AuthorStreamState", BindingFlags.NonPublic)!;
        private static readonly FieldInfo AuthorStreamsField =
            MuxerType.GetField("_authorStreams", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo RegisterMethod =
            MuxerType.GetMethod("RegisterAuthorStream", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo ShouldEmitMethod =
            MuxerType.GetMethod("ShouldEmitFrame", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo UpdateOffsetMethod =
            MuxerType.GetMethod("UpdateLastEmittedOffset", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly PropertyInfo StreamIdProp = StateType.GetProperty("StreamId")!;

        private readonly LiveStreamMuxer _muxer;
        private readonly object _authorStreams; // ConcurrentDictionary<AuthorId, AuthorStreamState>

        public MuxerHarness()
        {
            var services = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Debug))
                .BuildServiceProvider();

            // Allocate without running constructor (which starts the worker)
            _muxer = (LiveStreamMuxer)RuntimeHelpers.GetUninitializedObject(MuxerType);

            // Initialize _authorStreams field
            AuthorStreamsField.SetValue(_muxer, Activator.CreateInstance(AuthorStreamsField.FieldType));
            _authorStreams = AuthorStreamsField.GetValue(_muxer)!;

            // Initialize backing fields for auto-properties used by RegisterAuthorStream
            // Services { get; } → <Services>k__BackingField
            var servicesBackingField = MuxerType.GetField("<Services>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            servicesBackingField.SetValue(_muxer, services);

            // Log uses field-backed property: field ??= Services.LogFor<LiveStreamMuxer>()
            // Just trigger it by calling a harmless method, or set it directly
            var logBackingField = MuxerType.GetField("<Log>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (logBackingField != null) {
                logBackingField.SetValue(_muxer, services.GetRequiredService<ILoggerFactory>().CreateLogger<LiveStreamMuxer>());
            }
        }

        public void Register(AuthorId authorId, string streamId, Moment beginsAt, CancellationTokenSource cts)
            => RegisterMethod.Invoke(_muxer, [authorId, streamId, beginsAt, cts]);

        public bool ShouldEmitFrame(AuthorId authorId, string streamId, AudioFrame frame)
            => (bool)ShouldEmitMethod.Invoke(_muxer, [authorId, streamId, frame])!;

        public void UpdateLastEmittedOffset(AuthorId authorId, string streamId, TimeSpan offset)
            => UpdateOffsetMethod.Invoke(_muxer, [authorId, streamId, offset]);

        /// <summary>
        /// Simulates the finally block cleanup logic:
        ///   if TryGetValue matches streamId → TryRemove
        /// </summary>
        public void SimulateFinally(AuthorId authorId, string streamId)
        {
            var dictType = _authorStreams.GetType();
            var tryGetValue = dictType.GetMethod("TryGetValue")!;
            var tryRemove = dictType.GetMethod("TryRemove",
                [typeof(AuthorId), StateType.MakeByRefType()])!;

            var getArgs = new object?[] { authorId, null };
            if ((bool)tryGetValue.Invoke(_authorStreams, getArgs)!)
            {
                var state = getArgs[1]!;
                if ((string)StreamIdProp.GetValue(state)! == streamId)
                {
                    var removeArgs = new object?[] { authorId, null };
                    tryRemove.Invoke(_authorStreams, removeArgs);
                }
            }
        }

        public string? GetActiveStreamId(AuthorId authorId)
        {
            var tryGetValue = _authorStreams.GetType().GetMethod("TryGetValue")!;
            var args = new object?[] { authorId, null };
            if (!(bool)tryGetValue.Invoke(_authorStreams, args)!)
                return null;
            return (string)StreamIdProp.GetValue(args[1]!)!;
        }

        public bool HasAuthorEntry(AuthorId authorId)
        {
            var containsKey = _authorStreams.GetType().GetMethod("ContainsKey")!;
            return (bool)containsKey.Invoke(_authorStreams, [authorId])!;
        }
    }
}
