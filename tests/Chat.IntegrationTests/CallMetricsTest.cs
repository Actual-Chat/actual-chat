using System.Diagnostics.Metrics;
using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class CallMetricsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task CanceledRingShouldCountAPlacedCallAndItsEnd()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        using var meters = new CallMeterListener();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.CancelCall(chatId, bob.Id, default);

        // assert
        meters.Count("calls.started", ("chat_kind", "Peer"), ("has_video", false), ("kind", "placed"))
            .Should().Be(1);
        meters.Count("calls.ended", ("outcome", "Canceled"), ("reason", "Canceled"), ("chat_kind", "Peer"))
            .Should().Be(1);
        meters.Count("calls.invites", ("status", "Missed"), ("ack", "None")).Should().Be(1);
        meters.Count("calls.dial_duration", ("outcome", "Canceled")).Should().Be(1);
        meters.Count("calls.duration").Should().Be(0, "a call that never connected has no talk time");
        meters.Count("calls.answer_delay").Should().Be(0);
    }

    [Fact]
    public async Task DeclinedCallShouldCountTheRingAckAndADeclinedEnd()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        using var meters = new CallMeterListener();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), true, default);
        await backend.ConfirmRing(chatId, alice.Id, RingAck.Ringing, default);
        await backend.DeclineCall(chatId, alice.Id, default);

        // assert
        meters.Count("calls.started", ("has_video", true)).Should().Be(1);
        meters.Count("calls.ended", ("outcome", "Declined"), ("reason", "Declined"), ("has_video", true))
            .Should().Be(1);
        meters.Count("calls.invites", ("status", "Declined"), ("ack", "Ringing")).Should().Be(1);
        meters.Count("calls.ring_delivery_delay").Should().Be(1, "the invitee's client confirmed the ring");
    }

    [Fact]
    public async Task AnsweredCallShouldRecordItsAnswerDelayAndTalkTime()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = (LiveSessionsBackend)tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        using var meters = new CallMeterListener();

        // act
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);
        await backend.AcceptCall(chatId, alice.Id, default);
        // A leave that leaves the call one party only schedules its close CallLeaveGrace later;
        // EnforceCallLeaveGrace is internal so the test runs that check now instead of waiting it out.
        await backend.SetParticipation(chatId, alice.Id, ParticipationKind.Record, false, default);
        await backend.EnforceCallLeaveGrace(chatId);

        // assert
        meters.Count("calls.ended", ("outcome", "Ended"), ("reason", "PartyLeft")).Should().Be(1);
        meters.Count("calls.invites", ("status", "Accepted")).Should().Be(1);
        meters.Count("calls.answer_delay").Should().Be(1);
        meters.Count("calls.duration", ("chat_kind", "Peer"), ("has_video", false)).Should().Be(1);
        meters.Values("calls.party_count").Should().Equal([2], "the caller and the one who answered");
        meters.Count("calls.dial_duration").Should().Be(0, "an answered call isn't a failed dial");
    }

    [Fact]
    public async Task CallerAlreadyInACallShouldCountARefusedStart()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        var (chatId, bob, alice) = await NewPeerChat(tester);
        var backend = tester.AppServices.GetRequiredService<ILiveSessionsBackend>();
        using var meters = new CallMeterListener();
        await backend.StartCall(chatId, bob.Id, new[] { alice.Id }.ToApiArray(), false, default);

        // act - Alice, who is being rung, tries to place a call of her own
        var startCall = () => backend.StartCall(chatId, alice.Id, new[] { bob.Id }.ToApiArray(), false, default);

        // assert
        await startCall.Should().ThrowAsync<InvalidOperationException>();
        meters.Count("calls.start_refused", ("reason", "caller_busy")).Should().Be(1);
        meters.Count("calls.started").Should().Be(1, "only Bob's call was placed");
        await backend.CancelCall(chatId, bob.Id, default);
    }

    // Private methods

    private static async Task<(ChatId ChatId, AuthorFull Bob, AuthorFull Alice)> NewPeerChat(IWebTester tester)
    {
        var bob = await tester.SignInAsUniqueBob();
        var alice = await tester.SignInAsUniqueAlice();
        await tester.CreatePeerContact(alice, bob);
        await tester.SignIn(bob);
        var chatId = (ChatId)PeerChatId.New(bob.Id, alice.Id);
        var authors = tester.AppServices.GetRequiredService<IAuthorsBackend>();
        var bobAuthor = await authors.EnsureJoined(chatId, bob.Id, default);
        var aliceAuthor = await authors.EnsureJoined(chatId, alice.Id, default);
        return (chatId, bobAuthor, aliceAuthor);
    }

    // Nested types

    private sealed class CallMeterListener : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<Measurement> _measurements = new();

        public CallMeterListener()
        {
            _listener.InstrumentPublished = (instrument, listener) => {
                if (instrument.Name.StartsWith("calls."))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>(OnMeasurement);
            _listener.SetMeasurementEventCallback<int>(OnMeasurement);
            _listener.SetMeasurementEventCallback<double>(OnMeasurement);
            _listener.Start();
        }

        public void Dispose()
            => _listener.Dispose();

        public int Count(string name, params (string Key, object Value)[] tags)
            => Find(name, tags).Count();

        public IEnumerable<double> Values(string name, params (string Key, object Value)[] tags)
            => Find(name, tags).Select(m => m.Value);

        private IEnumerable<Measurement> Find(string name, (string Key, object Value)[] tags)
            => _measurements.Where(m => m.Name == name
                && tags.All(t => m.Tags.TryGetValue(t.Key, out var value) && Equals(value, t.Value)));

        private void OnMeasurement<T>(
            Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
            where T : struct
        {
            var tagMap = new Dictionary<string, object?>();
            foreach (var tag in tags)
                tagMap[tag.Key] = tag.Value;
            _measurements.Enqueue(new Measurement(instrument.Name, Convert.ToDouble(value), tagMap));
        }

        private sealed record Measurement(string Name, double Value, IReadOnlyDictionary<string, object?> Tags);
    }
}
