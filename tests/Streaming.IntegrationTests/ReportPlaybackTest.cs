using ActualChat.Chat;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ReportPlaybackTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task FindEntryIdByAudioStreamId_ResolvesStreamingEntry()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var (chat, entry, streamId) = await CreateChatWithStreamingAudioEntry(session, "FindEntryTest");

        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var foundId = await chatsBackend.FindEntryIdByAudioStreamId(chat.Id, streamId, CancellationToken.None);
        foundId.Should().Be(entry.Id);

        var missingId = await chatsBackend.FindEntryIdByAudioStreamId(chat.Id, "no-such-stream", CancellationToken.None);
        missingId.Should().BeNull();
    }

    private async Task<(Chat.Chat Chat, ChatEntry Entry, string StreamId)> CreateChatWithStreamingAudioEntry(
        Session session, string title)
    {
        var services = AppHost.Services;
        var commander = services.Commander();
        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = title,
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, chat.Id, CancellationToken.None);
        author.Require();

        var streamId = $"test-audio-{Guid.NewGuid():N}";
        var entryId = ChatEntryId.New(chat.Id, 0);
        var entry = await commander.Call(new ChatsBackend_ChangeEntry(
            entryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "",
                Audio = new ChatEntryAudio { StreamId = streamId },
                BeginsAt = services.Clocks().SystemClock.Now,
            })));
        return (chat, entry, streamId);
    }
}
