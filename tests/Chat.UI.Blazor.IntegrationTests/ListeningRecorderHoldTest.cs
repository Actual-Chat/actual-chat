using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class ListeningRecorderHoldTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListeningShouldWaitForRecorderPresenceToSynchronize(bool cancelBeforeAnswer)
    {
        // arrange
        using var appHost = await NewAppHost("chat-ui", o => o with {
            MustInitializeDb = false,
            ConfigureServices = (_, services) => services.AddScoped<LiveStreamUI, DelayedLiveStreamUI>(),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var hub = tester.ScopedAppServices.AppUIHub();
        var streams = (DelayedLiveStreamUI)hub.LiveStreamUI;
        await hub.ChatAudioUI.SetRecordingChatId(chatId);
        await hub.ChatAudioUI.SetRecordingChatId(null);
        await TestWait.When(async _ => (await hub.ChatAudioUI.GetRecordingChatId()).Should().BeNull());
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // act
        using var scope = ComputedSynchronizer.None.Instance.Activate();
        var watcher = hub.ChatAudioUI.StopListeningWhenIdle(chatId, cts.Token);
        try {
            await TestWait.WhenPolled(() => {
                (streams.Recorder.WhenSynchronizationRequested.IsCompleted || watcher.IsCompleted).Should().BeTrue();
                return Task.CompletedTask;
            });

            // assert
            watcher.IsCompleted.Should().BeFalse("a default false is not a server verdict that nobody is recording");
            (await hub.ChatAudioUI.GetListeningChatIds()).Should().Contain(chatId);
            streams.Recorder.WhenSynchronizationRequested.IsCompleted.Should().BeTrue();
            if (!cancelBeforeAnswer) {
                streams.Recorder.Answer(false);
                await TestWait.When(async ct => {
                    var activeChats = await hub.ActiveChatsUI.ActiveChats.Use(ct);
                    activeChats.Should().NotContain(c => c.ChatId == chatId && c.IsListening);
                });
                await watcher;
            }
        }
        finally {
            await cts.CancelAsync();
            await watcher;
        }

        if (cancelBeforeAnswer)
            hub.ActiveChatsUI.ActiveChats.Value.Should().Contain(c => c.ChatId == chatId && c.IsListening);
    }

    [Fact]
    public async Task MutingShouldKeepListeningUntilTheLastRecorderStops()
    {
        // arrange
        await using var bob = AppHost.NewBlazorTester(Out);
        await using var alice = AppHost.NewBlazorTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();
        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);
        var aliceAuthor = await alice.GetOwnAuthor(chatId);
        var backend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var hub = bob.ScopedAppServices.AppUIHub();
        await backend.SetParticipation(chatId, aliceAuthor!.Id, ParticipationKind.Record, true, default);
        await TestWait.When(async ct => (await hub.LiveStreamUI.HasRecorder(chatId, ct)).Should().BeTrue());
        await hub.ChatAudioUI.SetRecordingChatId(chatId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var watcher = Task.CompletedTask;
        try {
            // act
            await hub.ChatAudioUI.SetRecordingChatId(null);
            watcher = hub.ChatAudioUI.StopListeningWhenIdle(chatId, cts.Token);

            // assert
            await TestWait.When(async _ => (await hub.ChatAudioUI.GetRecordingChatId()).Should().BeNull());
            (await hub.ChatAudioUI.GetListeningChatIds()).Should().Contain(chatId);
            watcher.IsCompleted.Should().BeFalse();

            // act
            await backend.SetParticipation(chatId, aliceAuthor.Id, ParticipationKind.Record, false, default);

            // assert
            await TestWait.When(async ct => {
                var activeChats = await hub.ActiveChatsUI.ActiveChats.Use(ct);
                activeChats.Should().NotContain(c => c.ChatId == chatId && c.IsListening);
            });
            await watcher;
        }
        finally {
            await cts.CancelAsync();
            await watcher;
        }
    }

    public class DelayedLiveStreamUI(AppUIHub hub) : LiveStreamUI(hub)
    {
        public UnsynchronizedRecorderState Recorder { get; } = new(hub.Services);
        private MutableState<bool> Activity { get; } = hub.StateFactory.NewMutable(false);

        public override Task<bool> HasActivity(ChatId chatId, CancellationToken cancellationToken)
            => Activity.Use(cancellationToken);

        public override Task<bool> HasRecorder(ChatId chatId, CancellationToken cancellationToken)
            => Recorder.Use(cancellationToken);
    }

    public sealed class UnsynchronizedRecorderState(IServiceProvider services)
        : MutableState<bool>(new Options(), services)
    {
        private readonly TaskCompletionSource _whenSynchronizationRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _whenServerAnswered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WhenSynchronizationRequested => _whenSynchronizationRequested.Task;

        public void Answer(bool hasRecorder)
        {
            Value = hasRecorder;
            _whenServerAnswered.TrySetResult();
        }

        public override Task WhenSynchronized(
            ComputedSynchronizer synchronizer, CancellationToken cancellationToken = default)
        {
            _whenSynchronizationRequested.TrySetResult();
            return _whenServerAnswered.Task.WaitAsync(cancellationToken);
        }
    }
}
