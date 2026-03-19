using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualChat.Video;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveBackendRedisStateTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private RedisDb<StreamingContext> RedisDb => AppHost.Services.GetRequiredService<RedisDb<StreamingContext>>();

    // --- Audio ---

    [Fact]
    public async Task AudioBackend_ShouldPersistStreamToRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioPersist");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Verify via ListActiveStreams
        var streams = await liveBackend.ListActiveStreams(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);

        // Verify directly in Redis
        var redisEntries = await ReadRedisHash<LiveStreamInfo>("live-audio:streams", chatId);
        redisEntries.Should().ContainKey(streamInfo.StreamId);
        redisEntries[streamInfo.StreamId].ChatId.Should().Be(chatId);
        redisEntries[streamInfo.StreamId].AuthorId.Should().Be(streamInfo.AuthorId);
    }

    [Fact]
    public async Task AudioBackend_ShouldRemoveStreamFromRedisOnUnregister()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioRemove");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);
        await liveBackend.UnregisterActiveStream(chatId, streamInfo.StreamId, CancellationToken.None);

        var redisEntries = await ReadRedisHash<LiveStreamInfo>("live-audio:streams", chatId);
        redisEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task AudioBackend_ShouldRecoverStateFromRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioRecover");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Verify it's in Redis
        var redisEntries = await ReadRedisHash<LiveStreamInfo>("live-audio:streams", chatId);
        redisEntries.Should().ContainKey(streamInfo.StreamId);

        // Invalidate Fusion cache to force re-read from Redis
        using (Invalidation.Begin())
            _ = liveBackend.ListActiveStreams(chatId, default);

        // ListActiveStreams should re-read from Redis
        var streams = await liveBackend.ListActiveStreams(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);
    }

    // --- Video ---

    [Fact]
    public async Task VideoBackend_ShouldPersistStreamToRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoPersist");
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var authorId = AuthorId.New(chatId, 1);
        var streamInfo = new VideoStreamInfo(streamId, chatId, authorId,
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);

        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        var streams = await liveBackend.ListActiveStreams(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamId);

        var redisEntries = await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId);
        redisEntries.Should().ContainKey(streamId.Value);
        redisEntries[streamId.Value].ChatId.Should().Be(chatId);
        redisEntries[streamId.Value].AuthorId.Should().Be(authorId);
    }

    [Fact]
    public async Task VideoBackend_ShouldRemoveStreamFromRedisOnUnregister()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoRemove");
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(streamId, chatId, AuthorId.New(chatId, 1),
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);

        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);
        await liveBackend.UnregisterActiveStream(chatId, streamId, CancellationToken.None);

        var redisEntries = await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId);
        redisEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task VideoBackend_ShouldPersistMembersToRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoMembers");
        var sessionId = $"session-{Guid.NewGuid():N}";
        var codecs = new ApiArray<string>(["av1", "h264"]);

        await liveBackend.RegisterVideoStreamMember(chatId, sessionId, codecs, CancellationToken.None);

        var redisEntries = await ReadRedisHash<ApiArray<string>>("live-video:members", chatId);
        redisEntries.Should().ContainKey(sessionId);
        redisEntries[sessionId].Should().BeEquivalentTo(codecs);
    }

    [Fact]
    public async Task VideoBackend_ShouldRemoveMemberFromRedisOnUnregister()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoMemberRemove");
        var sessionId = $"session-{Guid.NewGuid():N}";

        await liveBackend.RegisterVideoStreamMember(chatId, sessionId,
            new ApiArray<string>(["av1", "h264"]), CancellationToken.None);
        await liveBackend.UnregisterVideoStreamMember(chatId, sessionId, CancellationToken.None);

        var redisEntries = await ReadRedisHash<ApiArray<string>>("live-video:members", chatId);
        redisEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task VideoBackend_ShouldRecoverStateFromRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoRecover");

        // Register a stream
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var authorId = AuthorId.New(chatId, 1);
        var streamInfo = new VideoStreamInfo(streamId, chatId, authorId,
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);
        await liveBackend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Register a member
        var sessionId = $"session-{Guid.NewGuid():N}";
        var codecs = new ApiArray<string>(["h264"]);
        await liveBackend.RegisterVideoStreamMember(chatId, sessionId, codecs, CancellationToken.None);

        // Verify Redis has the data
        (await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId)).Should().NotBeEmpty();
        (await ReadRedisHash<ApiArray<string>>("live-video:members", chatId)).Should().NotBeEmpty();

        // Invalidate Fusion cache to force re-read from Redis
        using (Invalidation.Begin()) {
            _ = liveBackend.ListActiveStreams(chatId, default);
            _ = liveBackend.GetVideoStreamMemberCount(chatId, default);
            _ = liveBackend.GetSupportedDecoderCodecs(chatId, default);
        }

        // Recovery: ListActiveStreams should re-read from Redis
        var streams = await liveBackend.ListActiveStreams(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamId);

        // Recovery: member count and codecs should also be recovered
        var memberCount = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        memberCount.Should().Be(1);

        var decoderCodecs = await liveBackend.GetSupportedDecoderCodecs(chatId, CancellationToken.None);
        decoderCodecs.Should().Contain("h264");
    }

    // --- Helpers ---

    private async Task<(ChatId ChatId, ILiveAudioBackend Backend)> CreateChatWithAudioBackend(string testName)
    {
        var chatId = await CreateTestChat(testName);
        var backend = AppHost.Services.GetRequiredService<ILiveAudioBackend>();
        return (chatId, backend);
    }

    private async Task<(ChatId ChatId, ILiveVideoBackend Backend)> CreateChatWithVideoBackend(string testName)
    {
        var chatId = await CreateTestChat(testName);
        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        return (chatId, backend);
    }

    private async Task<ChatId> CreateTestChat(string testName)
    {
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull(testName));
        var chat = await Commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = $"RedisStateTest-{testName}",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        return chat.Id;
    }

    private static LiveStreamInfo NewAudioStreamInfo(ChatId chatId)
        => new() {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = $"test-{Guid.NewGuid():N}",
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

    private async Task<Dictionary<string, TValue>> ReadRedisHash<TValue>(string keyPrefix, ChatId chatId)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var key = $"{keyPrefix}:{chatId}";
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);
        var result = new Dictionary<string, TValue>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries) {
            var field = entry.Name.ToString();
            var value = MemoryPackSerializer.Deserialize<TValue>((byte[])entry.Value!);
            if (value != null)
                result[field] = value;
        }
        return result;
    }
}
