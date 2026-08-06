using ActualChat.Transcription.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(RetranscribeDisabledNotifyCollection))]
public class RetranscribeDisabledNotifyFlowTest(
    RetranscribeDisabledNotifyCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<RetranscribeDisabledNotifyCollection.AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ShouldDeliverPushWithPreviewTextWhenRetranscriptionDisabled()
    {
        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "Voice chat (retranscribe-disabled)");
        await Tester.InviteToChat(chat.Id, alice);
        await Tester.ForceOffline(alice);
        await Tester.SignIn(bob);

        var entry = await Tester.RecordVoiceEntry(chat.Id, Languages.English);

        // OpenAITranscriber is not registered when retranscription is disabled,
        // so ProcessAudio falls back to realtime transcript only.
        var notification = await Tester.WaitForChatEntryNotification(alice.Id, entry.Id);
        notification.Text.Should().NotBeNullOrEmpty();
        notification.EntryId.Should().Be(entry.Id);
    }
}

[CollectionDefinition(nameof(RetranscribeDisabledNotifyCollection))]
public sealed class RetranscribeDisabledNotifyCollection : ICollectionFixture<RetranscribeDisabledNotifyCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "retranscribe-disabled-notify",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<TranscriptionSettings>((x => x.UseFakeTranscriber, "true"));
                },
            });
}
