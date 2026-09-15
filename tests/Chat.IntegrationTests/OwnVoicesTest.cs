using ActualChat.Streaming;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.JSInterop;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public sealed class OwnVoicesTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    // 20 ms frames, so 600 of them make a 12 s entry - short of the 30 s an auto sample needs
    private const int EntryFrameCount = 600;
    private static readonly TimeSpan EntryDuration = TimeSpan.FromSeconds(12);

    // The RPC client's scope resolves JS interop at startup; a strict mock says nothing may use it
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out,
        services => services.AddSingleton(Mock.Of<IJSRuntime>(MockBehavior.Strict)));
    private IOwnVoices OwnVoices => field ??= Tester.AppServices.GetRequiredService<IOwnVoices>();
    private VoicePool Pool => field ??= AppHost.Services.GetRequiredService<VoicePool>();

    [Fact(Timeout = 120_000)]
    public async Task StatusShouldBeOffUntilTheUserOptsIn()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var ct = CancellationToken.None;

        // act
        var status = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct);

        // assert
        status.Should().Be(OwnVoiceStatus.Off);
    }

    [Fact(Timeout = 120_000)]
    public async Task StatusShouldSayHowMuchSpeechTheAutoSampleStillNeeds()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        await RegisterView(chatId);
        await Tester.RecordVoiceEntry(chatId, Languages.English, frameCount: EntryFrameCount);
        var ct = CancellationToken.None;
        var before = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct);

        // act
        await SetSettings(x => x with { IsOwnVoiceEnabled = true });

        // assert
        before.IsEnabled.Should().BeFalse();
        var status = await ComputedTest.When(async ct1 => {
            var s = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct1);
            s.IsEnabled.Should().BeTrue("the compute method must pick up the settings change");
            return s;
        });
        status.Status.Should().Be(UserVoiceStatus.None, "nothing asked the pool for a clone yet");
        status.Failure.Should().Be(VoiceSampleFailure.NotEnoughRecordings, "12 s is short of the 30 s minimum");
        status.HasExplicitSample.Should().BeFalse();
        status.MissingDuration.Should().NotBeNull();
        status.MissingDuration!.Value.Should().BeCloseTo(
            Constants.Audio.VoiceSampleMinDuration - EntryDuration,
            TimeSpan.FromSeconds(1),
            "what's missing is the minimum less the one 12 s recording");
    }

    [Fact(Timeout = 120_000)]
    public async Task StatusShouldFollowAnExplicitSampleThroughToAReadyClone()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var ct = CancellationToken.None;
        var client = Tester.ClientServices.GetRequiredService<IOwnVoices>();

        // act - opt in with an explicit sample, then let a dub acquire the clone
        await Tester.OptInOwnVoice(chatId, Languages.English, EntryFrameCount);
        var withSample = await ComputedTest.When(async ct1 => {
            var s = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct1);
            s.HasExplicitSample.Should().BeTrue();
            return s;
        });
        var voiceId = await Pool.Acquire(account.Id, ct);

        // assert
        withSample.IsEnabled.Should().BeTrue();
        withSample.Failure.Should().Be(VoiceSampleFailure.None, "the sample media exists");
        withSample.MissingDuration.Should().BeNull("an explicit sample has no auto-selection shortfall");
        withSample.Status.Should().Be(UserVoiceStatus.None);
        voiceId.Should().NotBeNullOrEmpty();
        await ComputedTest.When(async ct1 => {
            var s = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct1);
            s.Status.Should().Be(UserVoiceStatus.Ready, "the status depends on the UserVoice record");
        });
        var overRpc = await client.GetOwnVoiceStatus(Tester.Session, ct);
        overRpc.Should().Be(new OwnVoiceStatus(true, UserVoiceStatus.Ready, VoiceSampleFailure.None, null, true),
            "the client sees the same status through the RPC client");

        // act - removing the sample falls back to the (insufficient) auto selection
        await SetSettings(x => x with { OwnVoiceSampleMediaId = null });

        // assert
        await ComputedTest.When(async ct1 => {
            var s = await OwnVoices.GetOwnVoiceStatus(Tester.Session, ct1);
            s.HasExplicitSample.Should().BeFalse();
            s.Failure.Should().Be(VoiceSampleFailure.NotEnoughRecordings);
            s.MissingDuration.Should().NotBeNull();
        });
    }

    // Private methods

    private Task RegisterView(ChatId chatId)
    {
        // What ChatView does when a group chat is opened - that's how it reaches the recency list
        var command = new ChatUsages_RegisterUsage {
            Session = Tester.Session,
            Kind = ChatUsageListKind.ViewedGroupChats,
            ChatId = chatId,
        };
        return Tester.AppServices.Commander().Call(command, CancellationToken.None);
    }

    private Task SetSettings(Func<UserLanguageSettings, UserLanguageSettings> update)
        => AppHost.Services.UserSettingsUI(Tester.Session)
            .UserLanguageSettings()
            .Update(update, CancellationToken.None);
}
