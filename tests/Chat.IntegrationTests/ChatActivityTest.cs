using ActualChat.UI.Blazor.App.Services;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatActivityCollection))]
public class ChatActivityTest(ChatActivityCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId => Constants.Chat.DefaultChatId;

    [Fact]
    public async Task BasicTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var services = tester.AppServices;
        var clientServices = tester.ScopedAppServices;
        var authors = services.GetRequiredService<IAuthors>();
        await tester.SignInAsBob();
        var session = tester.Session;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;
        try {
            var chatActivity = clientServices.GetRequiredService<ChatActivity>();
            using var recordingActivity = await chatActivity.GetStreamingActivity(TestChatId, ct);
            var cStreamingEntries = await Computed.Capture(() => recordingActivity.GetStreamingEntries(ct), ct);
            var cStreamingAuthorIds = await Computed.Capture(() => recordingActivity.GetStreamingAuthorIds(ct), ct);
            cStreamingEntries.Value.Count.Should().Be(0);

            var tcs1 = new TaskCompletionSource();
            var tcs2 = new TaskCompletionSource();

            _ = Task.Run(() => AddChatEntries(session, authors, tcs1.Task, tcs2.Task, ct), ct);

            cStreamingEntries.Value.Count.Should().Be(0);
            cStreamingAuthorIds.Value.Count.Should().Be(0);

            // Step1
            tcs1.SetResult();

            await cStreamingEntries.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            cStreamingAuthorIds = await cStreamingAuthorIds.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
            var authorId = cStreamingAuthorIds.Value.Single();
            var cIsAuthorActive = await Computed.Capture(() => recordingActivity.IsAuthorStreaming(authorId, ct), ct);
            await cIsAuthorActive.When(x => x, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);

            // Step2
            tcs2.SetResult();

            await cStreamingEntries.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            await cStreamingAuthorIds.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
            await cIsAuthorActive.When(x => !x, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
        }
        finally {
            await cts.CancelAsync();
        }
    }

    private async Task AddChatEntries(
        Session session,
        IAuthors authors,
        Task step1,
        Task step2,
        CancellationToken cancellationToken)
    {
        await step1.ConfigureAwait(false);

        var author = await authors.EnsureJoined(session, TestChatId, CancellationToken.None).ConfigureAwait(false);
        var clock = MomentClockSet.Default.SystemClock;
        var id = new ChatEntryId(TestChatId, ChatEntryKind.Audio, 0, AssumeValid.Option);
        var entry = new ChatEntry(id) {
            AuthorId = author.Id,
            Content = "",
            StreamId = "FAKE",
            BeginsAt = clock.Now + TimeSpan.FromMilliseconds(20),
            ClientSideBeginsAt = clock.Now,
        };
        var commander = authors.GetCommander();
        var createCommand = new ChatsBackend_ChangeEntry(id, null, Change.Create(new ChatEntryDiff(entry)));
        entry = await commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);

        await step2.ConfigureAwait(false);

        var endsAt = clock.Now;
        var completeCommand = new ChatsBackend_ChangeEntry(
            entry.Id,
            entry.Version,
            Change.Update(new ChatEntryDiff {
                EndsAt = endsAt,
                StreamId = Symbol.Empty,
            }));
        entry = await commander.Call(completeCommand, true, cancellationToken).ConfigureAwait(false);

        entry.StreamId.Should().Be(Symbol.Empty);
        entry.EndsAt.Should().Be(endsAt);
    }
}
