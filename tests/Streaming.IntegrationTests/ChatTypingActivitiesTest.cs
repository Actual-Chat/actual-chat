using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ChatTypingActivitiesTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IChatTypingActivitiesBackend Backend
        => AppHost.Services.GetRequiredService<IChatTypingActivitiesBackend>();

    [Fact]
    public async Task ListsTypingAuthor()
    {
        var chatId = await CreateTestChat("ListsTyping");
        var authorId = AuthorId.New(chatId, 1);

        await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, CancellationToken.None);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);
    }

    [Fact]
    public async Task NoneKindStopsTyping()
    {
        var chatId = await CreateTestChat("NoneStops");
        var authorId = AuthorId.New(chatId, 1);

        await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, CancellationToken.None);
        await Backend.SetTyping(chatId, authorId, TypingActivityKind.None, CancellationToken.None);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().BeEmpty();
    }

    [Fact]
    public async Task OrdersByWhoStartedFirst()
    {
        var chatId = await CreateTestChat("OrdersByStart");
        var first = AuthorId.New(chatId, 1);
        var second = AuthorId.New(chatId, 2);

        await Backend.SetTyping(chatId, first, TypingActivityKind.Typing, CancellationToken.None);
        await Task.Delay(50);
        await Backend.SetTyping(chatId, second, TypingActivityKind.Typing, CancellationToken.None);
        // A keep-alive re-emit must not move the first author to the end of the queue.
        await Backend.SetTyping(chatId, first, TypingActivityKind.Typing, CancellationToken.None);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(first, second);
    }

    [Fact]
    public async Task LapsesWithoutKeepAlive()
    {
        var chatId = await CreateTestChat("Lapses");
        var authorId = AuthorId.New(chatId, 1);

        await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, CancellationToken.None);
        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);

        // The streak lapses on its own: nothing sends an explicit stop here.
        await ComputedTest.When(async ct => {
            var current = await Backend.ListTypingAuthorIds(chatId, ct);
            current.Should().BeEmpty();
        }, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task TypingResumesAfterLapse()
    {
        var chatId = await CreateTestChat("ResumesAfterLapse");
        var authorId = AuthorId.New(chatId, 1);

        await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, CancellationToken.None);
        await ComputedTest.When(async ct => {
            var current = await Backend.ListTypingAuthorIds(chatId, ct);
            current.Should().BeEmpty();
        }, TimeSpan.FromSeconds(20));

        await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, CancellationToken.None);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);
    }

    [Fact]
    public async Task LapsesOneAuthorWhileAnotherKeepsTyping()
    {
        var chatId = await CreateTestChat("LapsesOne");
        var quitter = AuthorId.New(chatId, 1);
        var persistent = AuthorId.New(chatId, 2);

        await Backend.SetTyping(chatId, quitter, TypingActivityKind.Typing, CancellationToken.None);
        await Backend.SetTyping(chatId, persistent, TypingActivityKind.Typing, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var keepAliveTask = KeepTyping(chatId, persistent, cts.Token);
        try {
            await ComputedTest.When(async ct => {
                var current = await Backend.ListTypingAuthorIds(chatId, ct);
                current.Should().Equal(persistent);
            }, TimeSpan.FromSeconds(20));
        }
        finally {
            await cts.CancelAsync();
            await keepAliveTask;
        }
    }

    // Private methods

    private async Task KeepTyping(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                await Backend.SetTyping(chatId, authorId, TypingActivityKind.Typing, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            // Expected: this is how the test stops the keep-alive
        }
    }

    private async Task<ChatId> CreateTestChat(string testName)
    {
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull(testName));
        var chat = await Commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = $"TypingActivitiesTest-{testName}",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();
        return chat.Id;
    }
}
