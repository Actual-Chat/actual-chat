using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class ChatActivityPanelModelTest
{
    [Fact]
    public void ArmedPttChatWithNothingToShowShouldHidePanel()
    {
        var model = NewModel(isPttArmed: true, hasOngoingCall: true);

        model.IsVisible.Should().BeFalse(
            "an open-but-silent mic renders nothing, so the panel would be blank");
    }

    [Fact]
    public void ArmedPttChatWithTalkerShouldShowPanel()
        => NewModel(isPttArmed: true, hasOngoingCall: true, isAnyoneTalking: true)
            .IsVisible.Should().BeTrue();

    [Fact]
    public void ArmedPttChatWithRemoteStreamsShouldShowPanel()
        => NewModel(isPttArmed: true, hasOngoingCall: true, hasRemoteStreams: true)
            .IsVisible.Should().BeTrue();

    [Fact]
    public void ArmedPttChatStreamingOwnVideoShouldShowPanel()
        => NewModel(isPttArmed: true, hasOngoingCall: true, isOwnVideoStreaming: true)
            .IsVisible.Should().BeTrue("the panel carries the hang-up button for own transmission");

    [Fact]
    public void ArmedPttChatRecordingHereShouldShowPanel()
        => NewModel(isPttArmed: true, hasOngoingCall: true, isRecordingHere: true)
            .IsVisible.Should().BeTrue();

    [Fact]
    public void LocationSharingShouldShowPanelEvenWithoutCallActivity()
        => NewModel(isPttArmed: true, isSharingOwnLocation: true)
            .IsVisible.Should().BeTrue("the panel carries the stop-sharing button");

    [Fact]
    public void UnarmedListeningChatShouldStillShowPanel()
        => NewModel(isPttArmed: false, isListening: true)
            .IsVisible.Should().BeTrue("a deliberate listen is an activity outside PTT arming");

    private static ChatActivityPanel.Model NewModel(
        bool isPttArmed = false,
        bool hasOngoingCall = false,
        bool isAnyoneTalking = false,
        bool hasRemoteStreams = false,
        bool isSharingOwnLocation = false,
        bool isListening = false,
        bool isOwnVideoStreaming = false,
        bool isRecordingHere = false)
        => new() {
            AudioState = new ChatAudioState(null, isListening, false, isRecordingHere),
            IsPttArmed = isPttArmed,
            HasOngoingCall = hasOngoingCall,
            IsAnyoneTalking = isAnyoneTalking,
            HasRemoteStreams = hasRemoteStreams,
            IsOwnVideoStreaming = isOwnVideoStreaming,
            IsWatchingHere = false,
            IsAudioDiagnosticsEnabled = false,
            IsSharingOwnLocation = isSharingOwnLocation,
        };
}
