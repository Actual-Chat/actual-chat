using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatActivityVisibilityTest
{
    [Fact]
    public void ArmedPttBareListenShouldNotCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isListening: true)
            .Should().BeFalse("arming pins IsListening on, so it's ambient state, not an activity");

    [Fact]
    public void UnarmedListenShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: false, isListening: true)
            .Should().BeTrue("a deliberate listen outside PTT arming is a real activity");

    [Fact]
    public void ArmedPttRecordingShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isRecording: true)
            .Should().BeTrue();

    [Fact]
    public void ArmedPttWithTalkerShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isListening: true, isAnyoneTalking: true)
            .Should().BeTrue();

    [Fact]
    public void ArmedPttWithOwnVideoShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isListening: true, isOwnVideoStreaming: true)
            .Should().BeTrue();

    [Fact]
    public void ArmedPttWithRemoteStreamsShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isListening: true, hasRemoteStreams: true)
            .Should().BeTrue();

    [Fact]
    public void ArmedPttSharingLocationShouldCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: true, isListening: true, isSharingOwnLocation: true)
            .Should().BeTrue();

    [Fact]
    public void IdleUnarmedChatShouldNotCountAsActivity()
        => ChatActivityVisibility.HasActivity(isPttArmed: false).Should().BeFalse();
}
