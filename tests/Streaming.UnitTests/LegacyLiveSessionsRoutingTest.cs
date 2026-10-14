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

    // Private methods

    private static LegacyName? LegacyNameOf(MethodInfo method, Version version)
        => new LegacyNames(method)[version];
}
