using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using ActualLab.Time.Testing;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection)), Trait("Category", nameof(ChatCollection))]
public class ChatActivityTest(AppHostFixture fixture, ITestOutputHelper @out)
    : AppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId => Constants.Chat.DefaultChatId;

    [Fact]
    public async Task BasicTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester();
        var services = tester.AppServices;
        var clientServices = tester.ScopedAppServices;
        var commander = services.GetRequiredService<ICommander>();
        var authors = services.GetRequiredService<IAuthors>();
        var account = await tester.SignInAsBob();
        var session = tester.Session;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, TestChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        try {
            var chatActivity = clientServices.GetRequiredService<ChatActivity>();
            using var recordingActivity = await chatActivity.GetStreamingActivity(TestChatId, ct);
            var cStreamingEntries = await Computed.Capture(() => recordingActivity.GetStreamingEntries(ct), ct);
            var cStreamingAuthorIds = await Computed.Capture(() => recordingActivity.GetStreamingAuthorIds(ct), ct);
            cStreamingEntries.Value.Count.Should().Be(0);

            // 2s pause, create entry, 2s pause, complete it
            _ = Task.Run(() => AddChatEntries(session, authors, ct), ct);

            await cStreamingEntries.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
            await cStreamingAuthorIds.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);

            await cStreamingEntries.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            cStreamingAuthorIds = await cStreamingAuthorIds.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(1), ct);
            var authorId = cStreamingAuthorIds.Value.Single();
            var cIsAuthorActive = await Computed.Capture(() => recordingActivity.IsAuthorStreaming(authorId, ct), ct);
            await cIsAuthorActive.When(x => x, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);

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
        CancellationToken cancellationToken)
    {
        using var testClock = new TestClock();
        await testClock.Delay(2000, cancellationToken);
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

        await testClock.Delay(2000, cancellationToken);

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
