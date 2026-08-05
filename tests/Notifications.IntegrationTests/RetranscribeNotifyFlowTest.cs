using ActualChat.Transcription.Module;
using ActualChat.Audio;
using ActualChat.Chat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Module;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(RetranscribeNotifyCollection))]
public class RetranscribeNotifyFlowTest(
    RetranscribeNotifyCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<RetranscribeNotifyCollection.AppHostFixture>(fixture, @out)
{
    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ShouldDeliverPushWithRetranscribedText()
    {
        FakeOfflineTranscriber.Behavior = static () => new Transcript(FakeOfflineTranscriber.FinalText, LinearMap.Zero, []);

        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "Voice chat");
        await Tester.InviteToChat(chat.Id, alice);
        await Tester.ForceOffline(alice);
        await Tester.SignIn(bob);

        var entry = await Tester.RecordVoiceEntry(chat.Id, Languages.English);

        var notification = await Tester.WaitForChatEntryNotification(alice.Id, entry.Id);
        notification.Text.Should().StartWith("Bobby: " + FakeOfflineTranscriber.Marker);
        notification.EntryId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ShouldDeliverPushEvenWhenRetranscriptionFails()
    {
        FakeOfflineTranscriber.Behavior = static () => throw new InvalidOperationException("simulated OpenAI failure");

        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "Voice chat (retranscribe-fails)");
        await Tester.InviteToChat(chat.Id, alice);
        await Tester.ForceOffline(alice);
        await Tester.SignIn(bob);

        var entry = await Tester.RecordVoiceEntry(chat.Id, Languages.English);

        var notification = await Tester.WaitForChatEntryNotification(alice.Id, entry.Id);
        notification.Text.Should().NotContain(FakeOfflineTranscriber.Marker);
        notification.Text.Should().NotBeNullOrEmpty();
        notification.EntryId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task ShouldDeliverPushForJustTextVoiceMessage()
    {
        var transcribeCallCount = 0;
        FakeOfflineTranscriber.Behavior = () => {
            Interlocked.Increment(ref transcribeCallCount);
            return new Transcript(FakeOfflineTranscriber.FinalText, LinearMap.Zero, []);
        };

        var alice = await Tester.SignInAsAlice();
        var bob = await Tester.SignInAsBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "Voice chat (just-text)");
        await Tester.InviteToChat(chat.Id, alice);
        await Tester.ForceOffline(alice);
        await Tester.SignIn(bob);

        var entry = await Tester.RecordVoiceEntry(chat.Id, Languages.English, VoiceMode.JustText);

        var notification = await Tester.WaitForChatEntryNotification(alice.Id, entry.Id);
        notification.Text.Should().NotContain(FakeOfflineTranscriber.Marker);
        notification.Text.Should().NotBeNullOrEmpty();
        notification.EntryId.Should().Be(entry.Id);
        transcribeCallCount.Should().Be(0, "no audio → no OpenAI retranscription should be triggered");
    }
}

[CollectionDefinition(nameof(RetranscribeNotifyCollection))]
public sealed class RetranscribeNotifyCollection : ICollectionFixture<RetranscribeNotifyCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "retranscribe-notify",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<TranscriptionSettings>(
                        (x => x.UseFakeTranscriber, "true"),
                        (x => x.OfflineRanking, "fake-offline"));
                },
                ConfigureServices = (_, services) => {
                    services.Replace(ServiceDescriptor.Singleton<IOfflineTranscriber>(_ => new FakeOfflineTranscriber()));
                },
            });
}

sealed file class FakeOfflineTranscriber : IOfflineTranscriber
{
    public TranscriberInfo Info { get; } = new() {
        Id = TranscriberId.NewBuiltin("fake-offline"),
        DriverId = "fake",
        Kind = TranscriberKind.OfflineOnly,
    };
    public const string Marker = "RefineTranscriptionTestMarker";
    public const string FinalText =
        Marker + " — final retranscribed text from the recording, made deliberately long to deterministically beat the realtime FakeTranscriber's canned templates regardless of template choice.";

    public static Func<Transcript?> Behavior { get; set; } =
        static () => new Transcript(FinalText, LinearMap.Zero, []);

    public Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Behavior());
}
