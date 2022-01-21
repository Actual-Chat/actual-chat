using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;

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
            _ = Task.Run(() => AddChatEntries(commander, chatAuthorsBackend, session, ct), ct);
            var chatActivity = clientServices.GetRequiredService<ChatActivity>();
            var recordingActivity = chatActivity.GetRecordingActivity(ChatId, ct);
            recordingActivity.Value.Should().HaveCount(0);

            await recordingActivity.Computed.WhenInvalidated(ct);
            await recordingActivity.Computed.Update(ct);
            recordingActivity.Value.Should().HaveCount(1);

            await recordingActivity.Computed.WhenInvalidated(ct).WithTimeout(TimeSpan.FromSeconds(10), cancellationToken: ct);
            await recordingActivity.Computed.Update(ct);
            recordingActivity.Value.Should().HaveCount(0);
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

        await Task.Delay(TimeSpan.FromMilliseconds(2000), cancellationToken);

        entry = entry with {
            EndsAt = clock.Now,
            StreamId = Symbol.Empty,
        };
        var completeCommand = new IChatsBackend.UpsertEntryCommand(entry);
        await commander.Call(completeCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
