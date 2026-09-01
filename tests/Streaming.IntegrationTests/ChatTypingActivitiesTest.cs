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

        await StartTyping(chatId, authorId);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);
    }

    [Fact]
    public async Task NoneKindStopsTyping()
    {
        var chatId = await CreateTestChat("NoneStops");
        var authorId = AuthorId.New(chatId, 1);

        await StartTyping(chatId, authorId);
        await StopTyping(chatId, authorId);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().BeEmpty();
    }

    [Fact]
    public async Task OrdersByWhoStartedFirst()
    {
        var chatId = await CreateTestChat("OrdersByStart");
        var first = AuthorId.New(chatId, 1);
        var second = AuthorId.New(chatId, 2);

        await StartTyping(chatId, first);
        await Task.Delay(50);
        await StartTyping(chatId, second);
        // A lease renewal must not move the first author to the end of the queue.
        await StartTyping(chatId, first);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(first, second);
    }

    [Fact]
    public async Task LapsesWithoutRenewal()
    {
        var chatId = await CreateTestChat("Lapses");
        var authorId = AuthorId.New(chatId, 1);

        await StartTyping(chatId, authorId);
        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);

        // The lease lapses on its own: nothing sends an explicit stop here.
        await WhenNobodyIsTyping(chatId, TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task TypingResumesAfterLapse()
    {
        var chatId = await CreateTestChat("ResumesAfterLapse");
        var authorId = AuthorId.New(chatId, 1);

        await StartTyping(chatId, authorId);
        await WhenNobodyIsTyping(chatId, TimeSpan.FromSeconds(20));

        await StartTyping(chatId, authorId);

        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);
    }

    [Fact]
    public async Task LapsesOneAuthorWhileAnotherKeepsTyping()
    {
        var chatId = await CreateTestChat("LapsesOne");
        var quitter = AuthorId.New(chatId, 1);
        var persistent = AuthorId.New(chatId, 2);

        await StartTyping(chatId, quitter);
        await StartTyping(chatId, persistent);

        using var cts = new CancellationTokenSource();
        var renewTask = KeepTyping(chatId, persistent, cts.Token);
        try {
            await ComputedTest.When(async ct => {
                var current = await Backend.ListTypingAuthorIds(chatId, ct);
                current.Should().Equal(persistent);
            }, TimeSpan.FromSeconds(20));
        }
        finally {
            await cts.CancelAsync();
            await renewTask;
        }
    }

    [Fact]
    public async Task HonorsShorterTtl()
    {
        var chatId = await CreateTestChat("ShorterTtl");
        var authorId = AuthorId.New(chatId, 1);

        await StartTyping(chatId, authorId, TimeSpan.FromSeconds(1));
        var authorIds = await Backend.ListTypingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().Equal(authorId);

        // Well before MaxTtl would have lapsed on its own
        await WhenNobodyIsTyping(chatId, Constants.Typing.MaxTtl);
    }

    [Fact]
    public async Task ClampsTtlToMax()
    {
        var chatId = await CreateTestChat("ClampsTtl");
        var authorId = AuthorId.New(chatId, 1);

        await StartTyping(chatId, authorId, TimeSpan.FromHours(1));

        await WhenNobodyIsTyping(chatId, TimeSpan.FromSeconds(20));
    }

    // Private methods

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

    private async Task KeepTyping(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        try {
            while (true) {
                await StartTyping(chatId, authorId, cancellationToken: cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        catch (OperationCanceledException) {
            // Expected: this is how the test stops the renewals
        }
    }

    private Task WhenNobodyIsTyping(ChatId chatId, TimeSpan timeout)
        => ComputedTest.When(async ct => {
            var authorIds = await Backend.ListTypingAuthorIds(chatId, ct);
            authorIds.Should().BeEmpty();
        }, timeout);

    private Task StartTyping(
        ChatId chatId,
        AuthorId authorId,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
        => Backend.SetTyping(
            chatId, authorId, TypingActivityKind.Typing, ttl ?? Constants.Typing.MaxTtl, cancellationToken);

    private Task StopTyping(ChatId chatId, AuthorId authorId)
        => Backend.SetTyping(chatId, authorId, TypingActivityKind.None, default, CancellationToken.None);
}
