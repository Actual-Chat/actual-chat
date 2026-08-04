using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GestureActivationPolicyTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(150);
    private static readonly ChatId ChatA = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly ChatId ChatB = ChatId.Parse("bbbbbbbbbbbbbbbbbbbb");
    private static readonly IReadOnlyDictionary<ChatId, Moment> NoVoice = new Dictionary<ChatId, Moment>();

    [Fact]
    public void SensesInsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(20) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeTrue();
    }

    [Fact]
    public void DoesNotSenseOutsideTheAnswerWindow()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(400) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void IgnoresVoiceInNonPttChats()
    {
        var last = new Dictionary<ChatId, Moment> { [ChatB] = T0 - TimeSpan.FromSeconds(5) };
        GestureActivationPolicy
            .ShouldSenseStartGestures(false, false, [ChatA], last, T0, Window)
            .Should().BeFalse();
    }

    [Fact]
    public void AlwaysOnSensesWithoutVoice()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [ChatA], NoVoice, T0, Window)
            .Should().BeTrue();

    [Fact]
    public void AlwaysOnStillNeedsAtLeastOnePttChat()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(true, false, [], NoVoice, T0, Window)
            .Should().BeFalse();

    [Fact]
    public void PracticeModeSensesWithNoPttChatsAtAll()
        => GestureActivationPolicy
            .ShouldSenseStartGestures(false, true, [], NoVoice, T0, Window)
            .Should().BeTrue();

    [Fact]
    public void AnswerWindowChatIsTheMostRecentOne()
    {
        // arrange
        var last = new Dictionary<ChatId, Moment> {
            [ChatA] = T0 - TimeSpan.FromSeconds(100),
            [ChatB] = T0 - TimeSpan.FromSeconds(20),
        };

        // act
        var answer = GestureActivationPolicy.GetAnswerWindowChat([ChatA, ChatB], last, T0, Window);

        // assert
        answer.Should().NotBeNull();
        answer!.Value.ChatId.Should().Be(ChatB);
        answer.Value.At.Should().Be(T0 - TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void AnswerWindowChatIgnoresStaleAndNonPttStamps()
    {
        // arrange
        var last = new Dictionary<ChatId, Moment> {
            [ChatA] = T0 - TimeSpan.FromSeconds(400),
            [ChatB] = T0 - TimeSpan.FromSeconds(1),
        };

        // act
        var answer = GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window);

        // assert
        answer.Should().BeNull();
    }

    [Fact]
    public void ClearingTheStampClosesTheAnswerWindow()
    {
        // What AudioWidget's Stop action does through IncomingVoiceActivityUI.ClearIncomingVoice:
        // without this the widget recomputes the identical state and the notification comes back.

        // arrange
        var last = new Dictionary<ChatId, Moment> { [ChatA] = T0 - TimeSpan.FromSeconds(5) };
        var beforeClear = GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window);

        // act
        last.Remove(ChatA);

        // assert
        beforeClear.Should().NotBeNull();
        GestureActivationPolicy.GetAnswerWindowChat([ChatA], last, T0, Window).Should().BeNull();
        GestureActivationPolicy.HasAnswerWindow([ChatA], last, T0, Window).Should().BeFalse();
    }

    [Theory]
    [InlineData(GestureKind.FlipToTalk, GestureRoute.StartReply)]
    [InlineData(GestureKind.DoubleShake, GestureRoute.StartReply)]
    [InlineData(GestureKind.FaceDown, GestureRoute.StopReply)]
    [InlineData(GestureKind.None, GestureRoute.None)]
    public void RoutesGesturesOutsidePracticeMode(GestureKind kind, GestureRoute expected)
        => GestureActivationPolicy.Route(kind, false).Should().Be(expected);

    [Fact]
    public void PracticeModeNeverTransmits()
    {
        foreach (var kind in Enum.GetValues<GestureKind>()) {
            var route = GestureActivationPolicy.Route(kind, true);
            route.Should().NotBe(GestureRoute.StartReply, $"{kind} must not open the mic in practice mode");
            route.Should().NotBe(GestureRoute.StopReply, $"{kind} must not touch the mic in practice mode");
        }
    }

    [Fact]
    public void PracticeModeRoutesRealGesturesToThePanel()
    {
        GestureActivationPolicy.Route(GestureKind.FlipToTalk, true).Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.DoubleShake, true).Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.FaceDown, true).Should().Be(GestureRoute.Practice);
        GestureActivationPolicy.Route(GestureKind.None, true).Should().Be(GestureRoute.None);
    }
}
