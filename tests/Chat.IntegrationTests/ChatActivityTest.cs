using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using Stl.Time.Testing;

namespace ActualChat.Chat.IntegrationTests;

public class ChatActivityTest : AppHostTestBase
{
    private const string ChatId = "the-actual-one";

    public ChatActivityTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        using var appHost = await NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var services = tester.AppServices;
        var clientServices = tester.ClientServices;
        var commander = services.GetRequiredService<ICommander>();
        var chatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();
        var user = await tester.SignIn(new User("Bob"));
        var session = tester.Session;
        var sessionProvider = clientServices.GetRequiredService<ISessionProvider>();
        sessionProvider.Session = session;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = cts.Token;
        try {
            var chatActivity = clientServices.GetRequiredService<ChatActivity>();
            using var recordingActivity = await chatActivity.GetRecordingActivity(ChatId, ct);
            var cActiveChatEntries = await Computed.Capture(() => recordingActivity.GetActiveChatEntries(ct));
            var cActiveAuthorIds = await Computed.Capture(() => recordingActivity.GetActiveAuthorIds(ct));
            cActiveChatEntries.Value.Count.Should().Be(0);

            // 2s pause, create entry, 2s pause, complete it
            _ = Task.Run(() => AddChatEntries(commander, chatAuthorsBackend, user.Id, ct), ct);

            await cActiveChatEntries.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
            await cActiveAuthorIds.When(x => x.Length == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);

            await cActiveChatEntries.When(x => x.Count == 1, ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            cActiveAuthorIds = await cActiveAuthorIds.When(x => x.Length == 1, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
            var authorId = cActiveAuthorIds.Value.Single();
            var cIsAuthorActive = await Computed.Capture(() => recordingActivity.IsAuthorActive(authorId, ct));
            await cIsAuthorActive.When(x => x, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);

            await cActiveChatEntries.When(x => x.Count == 0, ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            await cActiveAuthorIds.When(x => x.Length == 0, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
            await cIsAuthorActive.When(x => !x, ct).WaitAsync(TimeSpan.FromSeconds(0.5), ct);
        }
        finally {
            cts.Cancel();
        }
    }

    private async Task AddChatEntries(
        ICommander commander,
        IChatAuthorsBackend chatAuthorsBackend,
        string userId,
        CancellationToken cancellationToken)
    {
        var testClock = new TestClock();
        await testClock.Delay(2000, cancellationToken);
        var author = await chatAuthorsBackend.GetOrCreate(ChatId, userId, true, CancellationToken.None).ConfigureAwait(false);
        var clock = MomentClockSet.Default.SystemClock;
        var entry = new ChatEntry {
            ChatId = ChatId,
            Type = ChatEntryType.Audio,
            AuthorId = author.Id,
            Content = "",
            StreamId = "FAKE",
            BeginsAt = clock.Now + TimeSpan.FromMilliseconds(20),
            ClientSideBeginsAt = clock.Now,
        };
        var createCommand = new IChatsBackend.UpsertEntryCommand(entry);
        entry = await commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);

        await testClock.Delay(2000, cancellationToken);

        entry = entry with {
            EndsAt = clock.Now,
            StreamId = Symbol.Empty,
        };
        var completeCommand = new IChatsBackend.UpsertEntryCommand(entry);
        await commander.Call(completeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
