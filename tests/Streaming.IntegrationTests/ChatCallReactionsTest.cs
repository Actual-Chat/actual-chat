using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ChatCallReactionsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private IChatCallReactionsBackend Backend
        => AppHost.Services.GetRequiredService<IChatCallReactionsBackend>();
    private IChatCallReactions Api
        => AppHost.Services.GetRequiredService<IChatCallReactions>();

    [Fact]
    public async Task SentReactionShouldBeListed()
    {
        // arrange
        var (_, chatId) = await CreateTestChat("Listed");
        var authorId = AuthorId.New(chatId, 1);

        // act
        await Backend.Send(chatId, authorId, Emojis.ThumbsUp, CancellationToken.None);

        // assert
        var reactions = await Backend.List(chatId, CancellationToken.None);
        reactions.Select(x => (x.AuthorId, x.Emoji)).Should().Equal((authorId, Emojis.ThumbsUp));
    }

    [Fact]
    public async Task NewerReactionShouldReplaceTheAuthorsPreviousOne()
    {
        // arrange
        var (_, chatId) = await CreateTestChat("Replaces");
        var authorId = AuthorId.New(chatId, 1);
        await Backend.Send(chatId, authorId, Emojis.ThumbsUp, CancellationToken.None);
        var first = (await Backend.List(chatId, CancellationToken.None)).Single();

        // act
        await Task.Delay(Constants.Call.ReactionMinInterval + TimeSpan.FromMilliseconds(100));
        await Backend.Send(chatId, authorId, Emojis.Love, CancellationToken.None);

        // assert
        var reaction = (await Backend.List(chatId, CancellationToken.None)).Single();
        reaction.Emoji.Should().Be(Emojis.Love);
        reaction.SentAt.Should().BeGreaterThan(first.SentAt, "a fresh SentAt is what makes the client animate it again");
    }

    [Fact]
    public async Task ReactionsTooCloseTogetherShouldBeDropped()
    {
        // arrange
        var (_, chatId) = await CreateTestChat("Throttled");
        var authorId = AuthorId.New(chatId, 1);
        await Backend.Send(chatId, authorId, Emojis.ThumbsUp, CancellationToken.None);

        // act
        await Backend.Send(chatId, authorId, Emojis.Love, CancellationToken.None);

        // assert
        var reaction = (await Backend.List(chatId, CancellationToken.None)).Single();
        reaction.Emoji.Should().Be(Emojis.ThumbsUp);
    }

    [Fact]
    public async Task EachAuthorShouldKeepTheirOwnReaction()
    {
        // arrange
        var (_, chatId) = await CreateTestChat("PerAuthor");
        var first = AuthorId.New(chatId, 1);
        var second = AuthorId.New(chatId, 2);

        // act
        await Backend.Send(chatId, first, Emojis.ThumbsUp, CancellationToken.None);
        await Backend.Send(chatId, second, Emojis.Fire, CancellationToken.None);

        // assert
        var reactions = await Backend.List(chatId, CancellationToken.None);
        reactions.Select(x => x.AuthorId).Should().Equal(first, second);
    }

    [Fact]
    public async Task ReactionShouldExpireOnItsOwn()
    {
        // arrange
        var (_, chatId) = await CreateTestChat("Expires");
        var authorId = AuthorId.New(chatId, 1);

        // act
        await Backend.Send(chatId, authorId, Emojis.ThumbsUp, CancellationToken.None);

        // assert
        await TestWait.When(async ct => {
            var reactions = await Backend.List(chatId, ct);
            reactions.Should().BeEmpty();
        }, Constants.Call.ReactionDuration + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task EmojiOutsideTheAllowedSetShouldBeRejected()
    {
        // arrange
        var (session, chatId) = await CreateTestChat("Disallowed");

        // act
        var send = () => Api.Send(session, chatId, Emojis.Poop, CancellationToken.None);

        // assert
        await send.Should().ThrowAsync<Exception>();
        (await Backend.List(chatId, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task MemberReactionShouldBeListedButOutsiderShouldBeIgnored()
    {
        // arrange
        var (session, chatId) = await CreateTestChat("Membership");
        var outsider = Session.New();
        _ = await AppHost.SignIn(outsider, new AccountFull("Outsider"));

        // act
        await Api.Send(outsider, chatId, Emojis.Fire, CancellationToken.None);
        await Api.Send(session, chatId, Emojis.ThumbsUp, CancellationToken.None);

        // assert
        var reactions = await Backend.List(chatId, CancellationToken.None);
        reactions.Select(x => x.Emoji).Should().Equal(Emojis.ThumbsUp);
    }

    // Private methods

    private async Task<(Session Session, ChatId ChatId)> CreateTestChat(string testName)
    {
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull(testName));
        var chat = await Commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = $"CallReactionsTest-{testName}",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();
        return (session, chat.Id);
    }
}
