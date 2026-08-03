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
}
