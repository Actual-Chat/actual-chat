using ActualChat.Testing.Host;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class GestureUITest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(20);

    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);
    private AppUIHub Hub => field ??= Tester.ScopedAppServices.AppUIHub();
    private GestureUI GestureUI => Hub.GestureUI;

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ActivationLoopCompletesAnIterationAndArmsPracticeGestures()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeFalse();
        GestureUI.Start();

        // act: practice mode is the one input that arms sensing with zero PTT chats,
        // so an armed recognizer proves the loop got past every reactive read
        GestureUI.IsPracticeMode = true;

        // assert
        await TestExt.When(() => {
            GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeTrue();
            GestureUI.RecognizerOptions.IsDoubleShakeEnabled.Should().BeTrue();
        }, WaitTimeout.Debuggable());

        // act
        GestureUI.IsPracticeMode = false;

        // assert: converges back to disarmed and stays there - no PTT chat is armed
        await TestExt.When(() => {
            GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeFalse();
            GestureUI.RecognizerOptions.IsDoubleShakeEnabled.Should().BeFalse();
            GestureUI.RecognizerOptions.IsFaceDownEnabled.Should().BeFalse();
        }, WaitTimeout.Debuggable());
        await Task.Delay(TimeSpan.FromSeconds(2).Debuggable());
        GestureUI.RecognizerOptions.Should().Be(new GestureOptions(false, false, false, ShakeSensitivity.Medium));
    }

    [Fact]
    public async Task DisarmingFlipToTalkTakesEffectWellBeforeTheIdleCheckPeriod()
    {
        // arrange
        await Tester.SignInAsUniqueBob();
        var (chatId, _) = await Tester.CreateChat(true);
        var walkieTalkieSettings = Hub.UserSettingsUI.UserWalkieTalkieSettings();
        await walkieTalkieSettings.Update(x => x with {
            PttChatIds = [chatId],
            AreGesturesAlwaysOn = true,
            IsFlipToTalkEnabled = true,
        });
        GestureUI.Start();
        await TestExt.When(
            () => GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeTrue(),
            WaitTimeout.Debuggable());

        // act: the loop just ran, so its wall-clock floor puts the next tick ~15s out -
        // anything faster than that proves the settings write woke it
        await Task.Delay(TimeSpan.FromSeconds(1).Debuggable());
        await walkieTalkieSettings.Update(x => x with { IsFlipToTalkEnabled = false });

        // assert
        await TestExt.When(
            () => GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeFalse(),
            TimeSpan.FromSeconds(6).Debuggable());
    }
}
