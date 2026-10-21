using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.UnitTests;

public class LegacyLiveSessionsRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Version LastOldPeer = new(2, 21, 9999);
    private static readonly Version FirstNewPeer = new(2, 22);
    private static ChatId TestChatId => Constants.Chat.DefaultChatId;

    [Theory]
    [InlineData(nameof(ILiveSessionsBackend.LegacyGetState), "GetState")]
    [InlineData(nameof(ILiveSessionsBackend.LegacyGetVisibleStartLid), "GetVisibleStartLid")]
    [InlineData(nameof(ILiveSessionsBackend.LegacyGetLiveConversation), "GetLiveConversation")]
    [InlineData(nameof(ILiveSessionsBackend.LegacyUpdateSummary), "UpdateSummary")]
    [InlineData(nameof(ILiveSessionsBackend.LegacySetContextStart), "SetContextStart")]
    public void AnOldPeerShouldReachTheLegacyMethod(string legacyMethodName, string wireName)
    {
        // act
        var oldPeer = LegacyNameOf(typeof(ILiveSessionsBackend).GetMethod(legacyMethodName)!, LastOldPeer);
        var newPeer = LegacyNameOf(typeof(ILiveSessionsBackend).GetMethod(legacyMethodName)!, FirstNewPeer);

        // assert
        oldPeer?.Name.Should().Be(wireName);
        newPeer.Should().BeNull();
    }

    [Fact]
    public void TheNewMethodsShouldStepAsideForAnOldPeer()
    {
        // act
        var getState = typeof(ILiveSessionsBackend).GetMethod(nameof(ILiveSessionsBackend.GetState))!;
        var getLiveConversation = typeof(ILiveSessionsBackend).GetMethod(
            nameof(ILiveSessionsBackend.GetLiveConversation), [typeof(ConversationId), typeof(CancellationToken)])!;

        // assert
        LegacyNameOf(getState, LastOldPeer)?.Name.Should().Be("GetState_Hidden");
        LegacyNameOf(getLiveConversation, LastOldPeer)?.Name.Should().Be("GetLiveConversation_Hidden");
        LegacyNameOf(getState, FirstNewPeer).Should().BeNull();
        LegacyNameOf(getLiveConversation, FirstNewPeer).Should().BeNull();
    }

    [Fact]
    public void TheLegacyPublicGetStateShouldAnswerToAnOldPeer()
    {
        // act
        var legacy = LegacyNameOf(typeof(ILiveSessions).GetMethod(nameof(ILiveSessions.LegacyGetState))!, LastOldPeer);
        var current = LegacyNameOf(typeof(ILiveSessions).GetMethod(nameof(ILiveSessions.GetState))!, LastOldPeer);

        // assert
        legacy?.Name.Should().Be("GetState");
        current?.Name.Should().Be("GetState_Hidden");
    }

    [Fact]
    public void TheClientIdLessGetMyCallShouldAnswerToAnOldPeer()
    {
        // arrange
        var legacyMethod = typeof(ILiveSessions).GetMethod(nameof(ILiveSessions.LegacyGetMyCall))!;

        // act
        var oldPeer = LegacyNameOf(legacyMethod, LastOldPeer);
        var newPeer = LegacyNameOf(legacyMethod, FirstNewPeer);

        // assert
        oldPeer?.Name.Should().Be("GetMyCall");
        newPeer.Should().BeNull();
        legacyMethod.GetParameters().Select(p => p.ParameterType).Should().Equal(
            [typeof(Session), typeof(CancellationToken)],
            "the wire name carries the parameter count, and 2.21 calls GetMyCall:2");
    }

    [Fact]
    public void ALegacyStateShouldKeepTheOldMeaningOfTheFields()
    {
        // arrange
        var host = AuthorId.New(TestChatId, 1);
        var open = new LiveConversation {
            Id = ConversationId.New(TestChatId, 120),
            ContextStartLid = 100,
            SummaryEndLid = 131,
            StartedAt = new Moment(10_000),
            SummarizedAt = new Moment(20_000),
            SummarizedEntryCount = 7,
            Title = "T",
        };
        var state = new LiveSessionState {
            ChatId = TestChatId,
            FirstSpeechLid = 110,
            FirstSpeechAt = new Moment(5_000),
            HostAuthorId = host,
            IsTranscriptionOn = true,
            QuietSince = new Moment(30_000),
            OpenConversation = open,
        };

        // act
        var legacy = LegacyLiveSessionState.From(state)!;

        // assert
        legacy.StartEntryLid.Should().Be(110);
        legacy.EndEntryLid.Should().Be(130, "the old end was the last summarized entry, inclusive");
        legacy.VisibleStartLid.Should().Be(120);
        legacy.ContextStartLid.Should().Be(100);
        legacy.StartedAt.Should().Be(new Moment(5_000));
        legacy.SessionStartedAt.Should().Be(new Moment(10_000));
        legacy.LastSummaryAt.Should().Be(new Moment(20_000));
        legacy.MessageCount.Should().Be(7);
        legacy.IsClosing.Should().BeTrue();
        legacy.ClosingAt.Should().Be(new Moment(30_000));
        legacy.Host.Should().Be(host);
        legacy.TranscriptionOn.Should().BeTrue();
        LegacyLiveSessionState.From(null).Should().BeNull();
    }

    [Fact]
    public void A221ClientShouldReadTheLegacyState()
    {
        // arrange
        var host = AuthorId.New(TestChatId, 1);
        var state = new LiveSessionState {
            ChatId = TestChatId,
            FirstSpeechLid = 110,
            HostAuthorId = host,
            Kind = LiveSessionKind.Call,
            HasTranscript = true,
        };
        var options = MessagePackByteSerializer.DefaultOptions;
        var bytes = MessagePackSerializer.Serialize(LegacyLiveSessionState.From(state), options);

        // act
        var read = MessagePackSerializer.Deserialize<LiveSessionState221>(bytes, options);

        // assert
        read.ChatId.Should().Be(TestChatId);
        read.StartEntryLid.Should().Be(110);
        read.Host.Should().Be(host);
        read.Kind.Should().Be(LiveSessionKind.Call);
    }

    // Private methods

    private static LegacyName? LegacyNameOf(MethodInfo method, Version version)
        => new LegacyNames(method)[version];

    // Nested types

    /// <summary>
    /// The <see cref="LiveSessionState"/> wire shape as the 2.21 client reads it.
    /// </summary>
    [DataContract, MessagePackObject]
    public sealed partial record LiveSessionState221
    {
        [DataMember(Order = 0), Key(0)]
        public ChatId ChatId { get; init; } = null!;
        [DataMember(Order = 1), Key(1)]
        public long StartEntryLid { get; init; }
        [DataMember(Order = 2), Key(2)]
        public long EndEntryLid { get; init; }
        [DataMember(Order = 3), Key(3)]
        public Moment StartedAt { get; init; }
        [DataMember(Order = 4), Key(4)]
        public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
        [DataMember(Order = 5), Key(5)]
        public string Title { get; init; } = "";
        [DataMember(Order = 6), Key(6)]
        public string Description { get; init; } = "";
        [DataMember(Order = 7), Key(7)]
        public string Summary { get; init; } = "";
        [DataMember(Order = 8), Key(8)]
        public bool TranscriptionOn { get; init; }
        [DataMember(Order = 9), Key(9)]
        public int MessageCount { get; init; }
        [DataMember(Order = 10), Key(10)]
        public Moment LastSummaryAt { get; init; }
        [DataMember(Order = 11), Key(11)]
        public bool StartNotificationSent { get; init; }
        [DataMember(Order = 12), Key(12)]
        public long Version { get; init; }
        [DataMember(Order = 13), Key(13)]
        public bool IsClosing { get; init; }
        [DataMember(Order = 14), Key(14)]
        public Moment? ClosingAt { get; init; }
        [DataMember(Order = 15), Key(15)]
        public AuthorId Host { get; init; } = null!;
        [DataMember(Order = 16), Key(16)]
        public SessionRules Rules { get; init; } = SessionRules.Default;
        [DataMember(Order = 17), Key(17)]
        public Moment? SessionStartedAt { get; init; }
        [DataMember(Order = 18), Key(18)]
        public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
        [DataMember(Order = 19), Key(19)]
        public long VisibleStartLid { get; init; }
        [DataMember(Order = 20), Key(20)]
        public long ContextStartLid { get; init; }
        [DataMember(Order = 21), Key(21)]
        public bool IsExpandedByDefault { get; init; }
        [DataMember(Order = 22), Key(22)]
        public CallOutcome Outcome { get; init; }
        [DataMember(Order = 23), Key(23)]
        public bool HasVideo { get; init; }
        [DataMember(Order = 24), Key(24)]
        public AuthorId? CallerId { get; init; }
    }
}
