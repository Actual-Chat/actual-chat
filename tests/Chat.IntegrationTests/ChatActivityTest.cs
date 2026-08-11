using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatActivityCollection))]
public class ChatActivityTest(ChatActivityCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId => Constants.Chat.DefaultChatId;

    [Fact(Skip = "Flaky")]
    public async Task BasicTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var services = tester.AppServices;
        var clientServices = tester.ScopedAppServices;
        var authors = services.GetRequiredService<IAuthors>();
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();
        await tester.SignInAsBob();
        var session = tester.Session;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;
        try {
            var liveStreamUI = clientServices.GetRequiredService<LiveStreamUI>();
            var author = await authors.EnsureJoined(session, TestChatId, ct);

            var cStreamingAuthorIds = await Computed.Capture(() => liveStreamUI.GetAudioStreamingAuthorIds(TestChatId, ct), ct);
            cStreamingAuthorIds.Value.Count.Should().Be(0);

            // Register an active stream
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = TestChatId,
                AuthorId = author.Id,
                StreamId = "test-stream-1",
                BeginsAt = MomentClockSet.Default.SystemClock.Now,
            };
            await liveBackend.Register(TestChatId, streamInfo, ct);

            // Verify stream is visible
            await cStreamingAuthorIds.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            var authorId = cStreamingAuthorIds.Value.Single();
            authorId.Should().Be(author.Id);

            var cIsAuthorStreaming = await Computed.Capture(() => liveStreamUI.IsAuthorStreamingAudio(TestChatId, authorId, ct), ct);
            await cIsAuthorStreaming.When(x => x, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);

            // Unregister the stream
            await liveBackend.Unregister(TestChatId, streamInfo.StreamId, ct);

            // Verify stream is gone
            await cStreamingAuthorIds.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            await cIsAuthorStreaming.When(x => !x, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
        }
        finally {
            await cts.CancelAsync();
        }
    }
}
