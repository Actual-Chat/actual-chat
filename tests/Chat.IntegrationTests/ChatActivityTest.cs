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
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var services = tester.AppServices;
        var clientServices = tester.ClientServices;
        var commander = services.GetRequiredService<ICommander>();
        var chatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();
        var user = await tester.SignIn(new User("", "Bob"));
        var session = tester.Session;
        var sessionProvider = clientServices.GetRequiredService<ISessionProvider>();
        sessionProvider.Session = session;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");

        var cts = new CancellationTokenSource();
        var ct = cts.Token;
        try {
            var chatActivity = clientServices.GetRequiredService<ChatActivities>();
            using var recordingActivity = await chatActivity.GetRecordingActivity(ChatId);
            recordingActivity.CurrentActivity.Value.Should().HaveCount(0);

            // creates a streaming entry and completes it in 2 secs
            _ = Task.Run(() => AddChatEntries(commander, chatAuthorsBackend, session, ct), ct);

            await recordingActivity.CurrentActivity.Computed.WhenInvalidated(ct);
            await recordingActivity.CurrentActivity.Computed.Update(ct);
            recordingActivity.CurrentActivity.Value.Should().HaveCount(1);

            await recordingActivity.CurrentActivity.Computed.WhenInvalidated(ct).WithTimeout(TimeSpan.FromSeconds(10), cancellationToken: ct);
            await recordingActivity.CurrentActivity.Computed.Update(ct);
            recordingActivity.CurrentActivity.Value.Should().HaveCount(1);

            await recordingActivity.CurrentActivity.Computed.WhenInvalidated(ct).WithTimeout(TimeSpan.FromSeconds(20), cancellationToken: ct);
            await recordingActivity.CurrentActivity.Computed.Update(ct);
            recordingActivity.CurrentActivity.Value.Should().HaveCount(0);
        }
        finally {
            cts.Cancel();
        }
    }

    [Fact]
    public async Task AuthorActivityTest()
    {
        using var appHost = await TestHostFactory.NewAppHost();
        using var tester = appHost.NewWebClientTester();
        var services = tester.AppServices;
        var clientServices = tester.ClientServices;
        var commander = services.GetRequiredService<ICommander>();
        var chatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();
        var user = await tester.SignIn(new User("", "Bob"));
        var session = tester.Session;
        var sessionProvider = clientServices.GetRequiredService<ISessionProvider>();
        sessionProvider.Session = session;

        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, ChatId, CancellationToken.None);
        chat.Should().NotBeNull();
        chat?.Title.Should().Be("The Actual One");


        var cts = new CancellationTokenSource();
        var ct = cts.Token;
        try {
            var author = await chatAuthorsBackend.GetOrCreate(session, ChatId, ct);
            var chatActivity = clientServices.GetRequiredService<ChatActivities>();
            using var recordingActivity = await chatActivity.GetAuthorRecordingActivity(ChatId, author!.Id);
            recordingActivity.Recording.Value.Should().BeFalse();

            // creates a streaming entry and completes it in 2 secs
            _ = Task.Run(() => AddChatEntries(commander, chatAuthorsBackend, session, ct), ct);

            await recordingActivity.Recording.Computed.WhenInvalidated(ct);
            await recordingActivity.Recording.Computed.Update(ct);
            recordingActivity.Recording.Value.Should().BeTrue();

            await recordingActivity.Recording.Computed.WhenInvalidated(ct).WithTimeout(TimeSpan.FromSeconds(10), cancellationToken: ct);
            await recordingActivity.Recording.Computed.Update(ct);
            recordingActivity.Recording.Value.Should().BeFalse();
        }
        finally {
            cts.Cancel();
        }
    }

    private async Task AddChatEntries(
        ICommander commander,
        IChatAuthorsBackend chatAuthorsBackend,
        Session session,
        CancellationToken cancellationToken)
    {
        var testClock = new TestClock();
        await testClock.Delay(2000, cancellationToken);
        var author = await chatAuthorsBackend.GetOrCreate(session, ChatId, CancellationToken.None).ConfigureAwait(false);
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
