using ActualChat.Audio;
using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class TranscribedTextLengthsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task TranscribedTextIsSummedPerAuthor()
    {
        // arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("Kate"));
        var chat = await CreateChat(session, nameof(TranscribedTextIsSummedPerAuthor));
        var author = await services.GetRequiredService<IAuthors>().GetOwn(session, chat.Id, default);
        author.Require();

        // act
        await AddVoiceEntry(chat.Id, author.Id, "Hello there");
        await AddVoiceEntry(chat.Id, author.Id, "How are you");

        // assert
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        var lengths = await liveSessions.GetTranscribedTextLengths(session, chat.Id, default);
        lengths[author.Id].Should().Be("Hello there".Length + "How are you".Length);
    }

    [Fact(Timeout = 60_000)]
    public async Task TypedMessagesDoNotCount()
    {
        // arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("Bobby"));
        var chat = await CreateChat(session, nameof(TypedMessagesDoNotCount));
        var author = await services.GetRequiredService<IAuthors>().GetOwn(session, chat.Id, default);
        author.Require();

        // act - no Audio on this one, so nobody could have heard it
        await services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chat.Id, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "Just typing",
                BeginsAt = services.Clocks().SystemClock.Now,
            })));

        // assert
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        var lengths = await liveSessions.GetTranscribedTextLengths(session, chat.Id, default);
        lengths.Should().BeEmpty();
    }

    [Fact(Timeout = 60_000)]
    public async Task SpeechOlderThanTheWindowIsNotCounted()
    {
        // arrange
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("Ann"));
        var chat = await CreateChat(session, nameof(SpeechOlderThanTheWindowIsNotCounted));
        var author = await services.GetRequiredService<IAuthors>().GetOwn(session, chat.Id, default);
        author.Require();
        var window = services.GetRequiredService<AudioSettings>().TranscribedTextWindow;

        // act
        var longAgo = services.Clocks().SystemClock.Now - window - TimeSpan.FromMinutes(1);
        await AddVoiceEntry(chat.Id, author.Id, "Ancient history", longAgo);

        // assert
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        var lengths = await liveSessions.GetTranscribedTextLengths(session, chat.Id, default);
        lengths.Should().BeEmpty();
    }

    // Private methods

    private async Task<Chat.Chat> CreateChat(Session session, string title)
    {
        var chat = await AppHost.Services.Commander().Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = title,
                Kind = ChatKind.Group,
            },
        }));
        return chat.Require();
    }

    private Task AddVoiceEntry(ChatId chatId, AuthorId authorId, string content, Moment? beginsAt = null)
    {
        var services = AppHost.Services;
        return services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chatId, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = authorId,
                Content = content,
                Audio = new ChatEntryAudio { StreamId = $"test-audio-{RandomStringGenerator.Default.Next()}" },
                BeginsAt = beginsAt ?? services.Clocks().SystemClock.Now,
            })));
    }
}
