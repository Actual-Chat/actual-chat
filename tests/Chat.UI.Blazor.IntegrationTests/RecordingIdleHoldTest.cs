using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class RecordingIdleHoldTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task OwnVoiceShouldHoldTheRecordingIdle()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "recording-idle-own-voice");
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        var recorder = Tester.ScopedAppServices.GetRequiredService<AudioRecorder>();
        var recorderState = (MutableState<AudioRecorderState>)recorder.State;
        (await chatAudioUI.HasRecordingActivity(chat.Id, CancellationToken.None)).Should().BeFalse();

        // act
        recorderState.Set(new AudioRecorderState(chat.Id) { IsRecording = true, IsVoiceActive = true });

        // assert
        await ComputedTest.When(async ct => {
            (await chatAudioUI.HasRecordingActivity(chat.Id, ct)).Should().BeTrue(
                "the user's own voice must hold the countdown before the server sees the stream");
        }, Timeout);

        // act: the utterance ends
        recorderState.Set(new AudioRecorderState(chat.Id) { IsRecording = true });

        // assert
        await ComputedTest.When(async ct => {
            (await chatAudioUI.HasRecordingActivity(chat.Id, ct)).Should().BeFalse();
        }, Timeout);
    }

    [Fact]
    public async Task AudiblePlaybackShouldHoldTheRecordingIdle()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "recording-idle-playback");
        var chatAudioUI = Tester.ScopedAppServices.GetRequiredService<ChatAudioUI>();
        chatAudioUI.Enable();
        await chatAudioUI.SetListeningState(chat.Id, true);
        ChatListeningPlayer? player = null;
        await ComputedTest.When(async ct => {
            player = await chatAudioUI.GetListeningPlayer(chat.Id, ct);
            player.Should().NotBeNull();
        }, Timeout);
        var isPlaying = (MutableState<bool>)player!.Playback.IsPlaying;
        (await chatAudioUI.HasRecordingActivity(chat.Id, CancellationToken.None)).Should().BeFalse();

        // act
        isPlaying.Set(true);

        // assert
        await ComputedTest.When(async ct => {
            (await chatAudioUI.HasRecordingActivity(chat.Id, ct)).Should().BeTrue(
                "what this device still plays is audible speech, whatever the server's live edge says");
        }, Timeout);

        // act: playback drains
        isPlaying.Set(false);

        // assert
        await ComputedTest.When(async ct => {
            (await chatAudioUI.HasRecordingActivity(chat.Id, ct)).Should().BeFalse();
        }, Timeout);
    }
}
