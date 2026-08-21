using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveActivityTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task HasActivityShouldFollowStreamRegistration()
    {
        // arrange
        var services = AppHost.Services;
        var (session, chatId) = await CreateChat("LiveActivityTest1");
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        using var cts = new CancellationTokenSource(Timeout);
        var cHasActivity = await Computed.Capture(() => liveSessions.HasActivity(session, chatId, cts.Token));
        cHasActivity.Value.Should().BeFalse("nobody is streaming yet");

        // act
        var streamInfo = NewStreamInfo(services, chatId);
        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // assert
        cHasActivity = await cHasActivity.When(x => x, cts.Token);

        await liveBackend.Unregister(chatId, streamInfo.StreamId, CancellationToken.None);
        await cHasActivity.When(x => !x, cts.Token);
    }

    [Fact]
    public async Task HasActivityShouldIncludeTextOnlyStreams()
    {
        // arrange
        var services = AppHost.Services;
        var (session, chatId) = await CreateChat("LiveActivityTest2");
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();
        var liveSessions = services.GetRequiredService<ILiveSessions>();
        using var cts = new CancellationTokenSource(Timeout);
        var cHasActivity = await Computed.Capture(() => liveSessions.HasActivity(session, chatId, cts.Token));
        cHasActivity.Value.Should().BeFalse();

        // act
        var streamInfo = NewStreamInfo(services, chatId) with { IsTextOnly = true };
        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // assert
        await cHasActivity.When(x => x, cts.Token);
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.IsTextOnly.Should().BeTrue();
    }

    [Fact]
    public async Task ListShouldNotInvalidateOnTextMessage()
    {
        // arrange
        var services = AppHost.Services;
        var commander = services.Commander();
        var (session, chatId) = await CreateChat("LiveActivityTest3");
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();
        using var cts = new CancellationTokenSource(Timeout);
        var cList = await Computed.Capture(() => liveBackend.List(chatId, cts.Token));
        cList.Value.Should().BeEmpty();

        // act
        await commander.Call(new Chats_UpsertEntry {
            Session = session,
            ChatId = chatId,
            LocalId = null,
            Text = "Hello",
        }, cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);

        // assert
        cList.IsConsistent().Should().BeTrue("a quiet chat's live-audio list must not depend on its entries");
    }

    // Private methods

    private async Task<(Session Session, ChatId ChatId)> CreateChat(string title)
    {
        var services = AppHost.Services;
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull(title));
        var chat = await services.Commander().Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = title,
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();
        return (session, chat.Id);
    }

    private static LiveAudioStreamInfo NewStreamInfo(IServiceProvider services, ChatId chatId)
        => new() {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };
}
