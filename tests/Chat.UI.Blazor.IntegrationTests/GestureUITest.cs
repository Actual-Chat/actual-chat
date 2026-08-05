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
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    private TestAppHost? _sensorAppHost;
    private BlazorTester? _sensorTester;
    private BlazorTester? _noSensorTester;

    private BlazorTester NoSensorTester => _noSensorTester ??= AppHost.NewBlazorTester(Out);
    private AppUIHub Hub => field ??= SensorTester.ScopedAppServices.AppUIHub();
    private GestureUI GestureUI => Hub.GestureUI;

    private BlazorTester SensorTester {
        get {
            // GestureUI.OnRun early-returns unless the feed reports an accelerometer or the host
            // is MAUI, so the shared web host can never run the loop these tests are about.
            if (_sensorTester is not null)
                return _sensorTester;

            _sensorAppHost ??= NewAppHost("chat-ui", o => o with {
                    MustInitializeDb = false,
                    ConfigureServices = (_, services) =>
                        services.AddScoped<SensorFeed>(_ => new AvailableSensorFeed()),
                })
                .GetAwaiter().GetResult();
            return _sensorTester = _sensorAppHost.NewBlazorTester(Out);
        }
    }

    protected override async Task DisposeAsync()
    {
        await _sensorTester.DisposeSilentlyAsync();
        await _noSensorTester.DisposeSilentlyAsync();
        _sensorAppHost.DisposeSilently();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ActivationLoopCompletesAnIterationAndArmsPracticeGestures()
    {
        // arrange
        await SensorTester.SignInAsUniqueBob();
        GestureUI.Feed.IsAccelerometerAvailable.Should().BeTrue();
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
        await Task.Delay(SettleDelay.Debuggable());
        GestureUI.RecognizerOptions.Should().Be(new GestureOptions(false, false, false, ShakeSensitivity.Medium));
    }

    [Fact]
    public async Task DisarmingFlipToTalkTakesEffectWellBeforeTheIdleCheckPeriod()
    {
        // arrange
        await SensorTester.SignInAsUniqueBob();
        var (chatId, _) = await SensorTester.CreateChat(true);
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

    [Fact]
    public async Task TheLoopRunsWhenTheFeedReportsAnAccelerometer()
    {
        // arrange
        await SensorTester.SignInAsUniqueBob();
        GestureUI.Feed.IsAccelerometerAvailable.Should().BeTrue();

        // act
        GestureUI.Start();
        GestureUI.IsPracticeMode = true;

        // assert
        await TestExt.When(
            () => GestureUI.RecognizerOptions.IsFlipToTalkEnabled.Should().BeTrue(),
            WaitTimeout.Debuggable());
    }

    [Fact]
    public async Task TheLoopStaysOffWithoutAnAccelerometerOnANonMauiHost()
    {
        // The "doesn't" half of the gate; the test above is the "runs" half.

        // arrange
        await NoSensorTester.SignInAsUniqueBob();
        var gestureUI = NoSensorTester.ScopedAppServices.AppUIHub().GestureUI;
        gestureUI.Feed.IsAccelerometerAvailable.Should().BeFalse();

        // act
        gestureUI.Start();
        gestureUI.IsPracticeMode = true;

        // assert
        await Task.Delay(SettleDelay.Debuggable());
        gestureUI.RecognizerOptions.Should().Be(new GestureOptions(false, false, false, ShakeSensitivity.Medium));
        gestureUI.SampleCount.Should().Be(0);
    }

    // Nested types

    private sealed class AvailableSensorFeed : SensorFeed
    {
        public override bool IsAccelerometerAvailable => true;
    }
}
