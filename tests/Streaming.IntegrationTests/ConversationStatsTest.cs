using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ConversationStatsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task NoLatchedSessionYieldsNull()
    {
        // arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("Kate"));
        var chat = await CreateChat(session, nameof(NoLatchedSessionYieldsNull));
        var author = await services.GetRequiredService<IAuthors>().GetOwn(session, chat.Id, default);
        author.Require();

        // act - voice entries alone don't latch a session; it takes 2+ authors streaming
        await AddVoiceEntry(chat.Id, author.Id, "Hello there");

        // assert
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        var stats = await liveSessions.GetConversationStats(session, chat.Id, default);
        stats.Should().BeNull();
    }

    [Fact(Timeout = 60_000)]
    public async Task TranscriptSizeIsSummedPerAuthor()
    {
        // arrange
        var (session, chat, authorId) = await NewLatchedSession(nameof(TranscriptSizeIsSummedPerAuthor));

        // act
        await AddVoiceEntry(chat.Id, authorId, "Hello there");
        await AddVoiceEntry(chat.Id, authorId, "How are you");

        // assert
        var stats = await GetStats(session, chat.Id);
        stats.Require().TranscriptSizes[authorId]
            .Should().Be("Hello there".Length + "How are you".Length);
    }

    [Fact(Timeout = 60_000)]
    public async Task SpeechDurationIsSummedPerAuthor()
    {
        // arrange
        var (session, chat, authorId) = await NewLatchedSession(nameof(SpeechDurationIsSummedPerAuthor));
        // The window is clamped to the session start, so these have to sit inside the session
        var now = AppHost.Services.Clocks().SystemClock.Now;

        // act
        await AddVoiceEntry(chat.Id, authorId, "Four seconds", now, TimeSpan.FromSeconds(4));
        await AddVoiceEntry(chat.Id, authorId, "Six seconds", now, TimeSpan.FromSeconds(6));

        // assert
        var stats = await GetStats(session, chat.Id);
        stats.Require().SpeechDurations[authorId].Should().BeApproximately(10, 0.5);
    }

    [Fact(Timeout = 60_000)]
    public async Task TypedMessagesDoNotCount()
    {
        // arrange
        var (session, chat, authorId) = await NewLatchedSession(nameof(TypedMessagesDoNotCount));

        // act - no Audio on this one, so nobody could have heard it
        await AppHost.Services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chat.Id, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = authorId,
                Content = "Just typing",
                BeginsAt = AppHost.Services.Clocks().SystemClock.Now,
            })));

        // assert
        var stats = await GetStats(session, chat.Id);
        stats.Require().TranscriptSizes.Should().BeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task SpeechOlderThanTheWindowIsNotCounted()
    {
        // arrange
        var (session, chat, authorId) = await NewLatchedSession(nameof(SpeechOlderThanTheWindowIsNotCounted));
        var window = AppHost.Services.GetRequiredService<AudioSettings>().ConversationWindow;

        // act
        var longAgo = AppHost.Services.Clocks().SystemClock.Now - window - TimeSpan.FromMinutes(1);
        await AddVoiceEntry(chat.Id, authorId, "Ancient history", longAgo, TimeSpan.FromSeconds(2));

        // assert
        var stats = await GetStats(session, chat.Id);
        stats.Require().TranscriptSizes.Should().BeEmpty();
        stats.Require().SpeechDurations.Should().BeEmpty();
    }

    // Private methods

    private async Task<(Session Session, Chat.Chat Chat, AuthorId AuthorId)> NewLatchedSession(string title)
    {
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("Kate"));
        var chat = await CreateChat(session, title);
        var author = await services.GetRequiredService<IAuthors>().GetOwn(session, chat.Id, default);
        author.Require();

        var otherSession = Session.New();
        _ = await AppHost.SignIn(otherSession, new AccountFull("Bobby"));
        await AppHost.Services.Commander().Call(new Authors_Join { Session = otherSession, ChatId = chat.Id });
        var otherAuthor = await services.GetRequiredService<IAuthors>().GetOwn(otherSession, chat.Id, default);
        otherAuthor.Require();

        // SessionStartedAt latches only once a second author is in - accepting a call is the
        // shortest path to that; ambient sessions get there via two registered audio streams.
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        await liveSessions.StartCall(session, chat.Id, default, false, default);
        await liveSessions.AcceptCall(otherSession, chat.Id, default);
        return (session, chat, author.Id);
    }

    private async Task<ConversationStats?> GetStats(Session session, ChatId chatId)
    {
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessions>();
        return await liveSessions.GetConversationStats(session, chatId, default);
    }

    private async Task<Chat.Chat> CreateChat(Session session, string title)
    {
        var chat = await AppHost.Services.Commander().Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = title,
                    Kind = ChatKind.Group,
                    IsPublic = true, // So the second author can join and latch the session
                },
            },
        });
        return chat.Require();
    }

    private Task AddVoiceEntry(
        ChatId chatId,
        AuthorId authorId,
        string content,
        Moment? beginsAt = null,
        TimeSpan? duration = null)
    {
        var services = AppHost.Services;
        var startsAt = beginsAt ?? services.Clocks().SystemClock.Now;
        return services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = authorId,
                Content = content,
                Audio = new ChatEntryAudio { StreamId = $"test-audio-{RandomStringGenerator.Default.Next()}" },
                BeginsAt = startsAt,
                EndsAt = startsAt + (duration ?? TimeSpan.FromSeconds(1)),
            })));
    }
}
