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

    [Fact]
    public async Task ReportPlayback_EntryIdPath_SetsHeardAndStat_LeavesReadUntouched()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Heidi"));
        var (chat, entry, _) = await CreateChatWithStreamingAudioEntry(session, "HeardEntryIdTest");
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry.Id.LocalId);

        var read = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Read, CancellationToken.None);
        read.EntryLid.Should().Be(0);

        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        await ComputedTest.When(async ct => {
            var stat = await chatsBackend.GetReadPositionsStat(chat.Id, ct);
            stat.Should().NotBeNull();
            stat!.TopReadPositions.Should()
                .Contain(p => p.UserId == account.Id && p.EntryLid == entry.Id.LocalId);
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ReportPlayback_StreamIdPath_ResolvesEntry()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Ivan"));
        var (chat, entry, streamId) = await CreateChatWithStreamingAudioEntry(session, "HeardStreamIdTest");
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, streamId, null, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry.Id.LocalId);
    }

    [Fact]
    public async Task ReportPlayback_NotArmed_DoesNothing()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Judy"));
        var (chat, entry, _) = await CreateChatWithStreamingAudioEntry(session, "HeardNotArmedTest");

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(0);
    }

    [Fact]
    public async Task ReportPlayback_IsForwardOnly()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        var account = await appHost.SignIn(session, new AccountFull("Kate"));
        var (chat, entry1, _) = await CreateChatWithStreamingAudioEntry(session, "HeardForwardOnlyTest");
        var author = await services.GetRequiredService<IAuthors>()
            .GetOwn(session, chat.Id, CancellationToken.None);
        author.Require();
        var entry2 = await services.Commander().Call(new ChatsBackend_ChangeEntry(
            ChatEntryId.New(chat.Id, 0),
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = author.Id,
                Content = "",
                Audio = new ChatEntryAudio { StreamId = $"test-audio-{Guid.NewGuid():N}" },
                BeginsAt = services.Clocks().SystemClock.Now,
            })));
        await Arm(account.Id, chat.Id);

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        await liveStreams.ReportPlayback(session, chat.Id, "", entry2.Id, CancellationToken.None);
        await liveStreams.ReportPlayback(session, chat.Id, "", entry1.Id, CancellationToken.None);

        var positionsBackend = services.GetRequiredService<IChatPositionsBackend>();
        var heard = await positionsBackend.Get(account.Id, chat.Id, ChatPositionKind.Heard, CancellationToken.None);
        heard.EntryLid.Should().Be(entry2.Id.LocalId);
    }

    private Task Arm(UserId userId, ChatId chatId)
        => AppHost.Services.GetRequiredService<IServerKvasBackend>()
            .ForUser(userId).UserWalkieTalkieSettings()
            .Update(x => x.WithPttChat(chatId));

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
