using ActualChat.Streaming.Module;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(DubbingTranslationCollection))]
public class SpeakerVoicesTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private SpeakerVoices SpeakerVoices => field ??= AppHost.Services.GetRequiredService<SpeakerVoices>();
    private VoicePool Pool => field ??= AppHost.Services.GetRequiredService<VoicePool>();
    private StreamingSettings Settings => field ??= AppHost.Services.GetRequiredService<StreamingSettings>();

    [Fact(Timeout = 60_000)]
    public async Task OptedInAndAcquirableSpeakerGetsTheClone()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        await Tester.OptInOwnVoice(chatId, Languages.Russian);
        var ct = CancellationToken.None;

        // act
        var whileCloning = await SpeakerVoices.Get(chatId, entry.AuthorId, ct);
        var cloneVoiceId = await Pool.AcquireSettled(account.Id, ct);
        var voiceId = await SpeakerVoices.Get(chatId, entry.AuthorId, ct);

        // assert
        cloneVoiceId.Should().NotBeNullOrEmpty();
        whileCloning.Should().NotBeNullOrEmpty("the stock voice speaks while the clone is made");
        whileCloning.Should().NotBe(cloneVoiceId);
        voiceId.Should().Be(cloneVoiceId, "an opted-in, acquirable speaker is dubbed with their clone");
    }

    [Fact(Timeout = 60_000)]
    public async Task NotOptedInSpeakerGetsTheStockVoice()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var userVoices = AppHost.Services.GetRequiredService<IUserVoicesBackend>();

        // act
        var voiceId = await SpeakerVoices.Get(chatId, entry.AuthorId, ct);

        // assert
        var record = await userVoices.Get(account.Id, ct);
        record.Should().BeNull("a speaker who never opted in is never handed to the pool");
        voiceId.Should().NotBeNull("an author's stock voice still resolves via DubVoiceAccents");
    }

    [Fact(Timeout = 60_000)]
    public async Task OptedInSpeakerWithNoQuotaGetsTheStockVoice()
    {
        // arrange
        var account = await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var entry = await Tester.RecordVoiceEntry(chatId, Languages.Russian);
        var ct = CancellationToken.None;
        var userVoices = AppHost.Services.GetRequiredService<IUserVoicesBackend>();
        Settings.SonioxVoiceQuota = 0;
        try {
            await Tester.OptInOwnVoice(chatId, Languages.Russian);

            // act
            var voiceId = await SpeakerVoices.Get(chatId, entry.AuthorId, ct);
            await Pool.WhenSettled();

            // assert
            voiceId.Should().NotBeNullOrEmpty("an author's stock voice still resolves via DubVoiceAccents");
            var record = await userVoices.Get(account.Id, ct);
            (record?.Status ?? UserVoiceStatus.None).Should().Be(UserVoiceStatus.None,
                "a full pool never hands out a clone");
        }
        finally {
            Settings.SonioxVoiceQuota = null;
        }
    }
}
