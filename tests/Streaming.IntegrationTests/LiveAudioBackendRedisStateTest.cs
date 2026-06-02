using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualChat.Video;
using ActualLab.Redis;
using StreamingContext = ActualChat.Streaming.Db.StreamingContext;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveAudioBackendRedisStateTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private RedisDb<StreamingContext> RedisDb => AppHost.Services.GetRequiredService<RedisDb<StreamingContext>>();
    private IByteSerializer RedisHashStoreSerializer => Serializers.MessagePack;

    // --- Audio ---

    [Fact]
    public async Task AudioBackend_ShouldPersistStreamToRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioPersist");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Verify via ListActiveStreams
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);

        // Verify directly in Redis
        var state = await ReadRedisValue<LiveAudioBackend.State>("live-audio:state", chatId);
        state.Should().NotBeNull();
        state!.Version.Should().BeGreaterThan(0);
        state.Streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);
        state.Streams[0].ChatId.Should().Be(chatId);
        state.Streams[0].AuthorId.Should().Be(streamInfo.AuthorId);
    }

    [Fact]
    public async Task AudioBackend_ShouldRemoveStreamFromRedisOnUnregister()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioRemove");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);
        await liveBackend.Unregister(chatId, streamInfo.StreamId, CancellationToken.None);

        var state = await ReadRedisValue<LiveAudioBackend.State>("live-audio:state", chatId);
        state.Should().NotBeNull();
        state!.Streams.Should().BeEmpty();
    }

    [Fact]
    public async Task AudioBackend_ShouldRecoverStateFromRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioRecover");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Verify it's in Redis
        var state = await ReadRedisValue<LiveAudioBackend.State>("live-audio:state", chatId);
        state.Should().NotBeNull();
        state!.Streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);

        // Invalidate Fusion cache to force re-read from Redis
        using (Invalidation.Begin())
            _ = liveBackend.List(chatId, default);

        // ListActiveStreams should re-read from Redis
        var streams = await liveBackend.List(chatId, CancellationToken.None);
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
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        var streams = await liveBackend.List(chatId, CancellationToken.None);
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
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);
        await liveBackend.Unregister(chatId, streamId, CancellationToken.None);

        var redisEntries = await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId);
        redisEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task VideoBackend_ShouldAllowSameAuthorCameraAndScreenCast()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoSameAuthorKinds");
        var authorId = AuthorId.New(chatId, 1);
        var camera = NewVideoStreamInfo(chatId, authorId, VideoSourceKind.Camera);
        var screenCast = NewVideoStreamInfo(chatId, authorId, VideoSourceKind.ScreenCast);

        await liveBackend.Register(chatId, camera, CancellationToken.None);
        await liveBackend.Register(chatId, screenCast, CancellationToken.None);

        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().HaveCount(2);
        streams.Should().Contain(s => s.StreamId == camera.StreamId && s.SourceKind == VideoSourceKind.Camera);
        streams.Should().Contain(s => s.StreamId == screenCast.StreamId && s.SourceKind == VideoSourceKind.ScreenCast);
    }

    [Fact]
    public async Task VideoBackend_ShouldReplaceSameAuthorSameKindOnly()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoReplaceSameKind");
        var authorId = AuthorId.New(chatId, 1);
        var oldCamera = NewVideoStreamInfo(chatId, authorId, VideoSourceKind.Camera);
        var screenCast = NewVideoStreamInfo(chatId, authorId, VideoSourceKind.ScreenCast);
        var newCamera = NewVideoStreamInfo(chatId, authorId, VideoSourceKind.Camera);

        await liveBackend.Register(chatId, oldCamera, CancellationToken.None);
        await liveBackend.Register(chatId, screenCast, CancellationToken.None);
        await liveBackend.Register(chatId, newCamera, CancellationToken.None);

        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().HaveCount(2);
        streams.Should().NotContain(s => s.StreamId == oldCamera.StreamId);
        streams.Should().Contain(s => s.StreamId == newCamera.StreamId && s.SourceKind == VideoSourceKind.Camera);
        streams.Should().Contain(s => s.StreamId == screenCast.StreamId && s.SourceKind == VideoSourceKind.ScreenCast);
    }

    [Fact]
    public async Task VideoBackend_ShouldRejectSecondScreenCastFromDifferentAuthor()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoSingleScreenCast");
        var first = NewVideoStreamInfo(chatId, AuthorId.New(chatId, 1), VideoSourceKind.ScreenCast);
        var second = NewVideoStreamInfo(chatId, AuthorId.New(chatId, 2), VideoSourceKind.ScreenCast);

        await liveBackend.Register(chatId, first, CancellationToken.None);
        var registerSecond = () => liveBackend.Register(chatId, second, CancellationToken.None);

        await registerSecond.Should().ThrowAsync<InvalidOperationException>();
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(first.StreamId);
    }

    [Fact]
    public async Task VideoBackend_ShouldPersistMembersToRedis()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoMembers");
        var sessionId = $"session-{Guid.NewGuid():N}";
        var codecs = new ApiArray<string>(["av1", "h264"]);

        await liveBackend.RegisterMember(chatId, sessionId, codecs, CancellationToken.None);

        var redisEntries = await ReadRedisHash<VideoStreamMemberInfo>("live-video:members", chatId);
        redisEntries.Should().ContainKey(sessionId);
        redisEntries[sessionId].SupportedDecoderCodecs.Should().BeEquivalentTo(codecs);
    }

    [Fact]
    public async Task VideoBackend_ShouldRemoveMemberFromRedisOnUnregister()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoMemberRemove");
        var sessionId = $"session-{Guid.NewGuid():N}";

        await liveBackend.RegisterMember(chatId, sessionId,
            new ApiArray<string>(["av1", "h264"]), CancellationToken.None);
        await liveBackend.UnregisterMember(chatId, sessionId, CancellationToken.None);

        var redisEntries = await ReadRedisHash<VideoStreamMemberInfo>("live-video:members", chatId);
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
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);
        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Register a member
        var sessionId = $"session-{Guid.NewGuid():N}";
        var codecs = new ApiArray<string>(["h264"]);
        await liveBackend.RegisterMember(chatId, sessionId, codecs, CancellationToken.None);

        // Verify Redis has the data
        (await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId)).Should().NotBeEmpty();
        (await ReadRedisHash<VideoStreamMemberInfo>("live-video:members", chatId)).Should().NotBeEmpty();

        // Invalidate Fusion cache to force re-read from Redis
        using (Invalidation.Begin()) {
            _ = liveBackend.List(chatId, default);
            _ = liveBackend.GetVideoStreamMemberCount(chatId, default);
            _ = liveBackend.GetSupportedCodecs(chatId, default);
        }

        // Recovery: ListActiveStreams should re-read from Redis
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamId);

        // Recovery: member count and codecs should also be recovered
        var memberCount = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        memberCount.Should().Be(1);

        var decoderCodecs = await liveBackend.GetSupportedCodecs(chatId, CancellationToken.None);
        decoderCodecs.Should().Contain("h264");
    }

    // --- Codec invalidation ---

    [Fact]
    public async Task VideoBackend_ShouldInvalidateCodecsOnMemberChange()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("CodecInvalidate");

        // Capture initial codec computed value (no members → all codecs available)
        var computed = await Computed.Capture(
            () => liveBackend.GetSupportedCodecs(chatId, CancellationToken.None));
        computed.Value.Should().Contain("av1");
        computed.Value.Should().Contain("h264");
        computed.IsConsistent().Should().BeTrue();

        // Register a member that only supports h264
        var sessionId = $"session-{Guid.NewGuid():N}";
        await liveBackend.RegisterMember(chatId, sessionId,
            new ApiArray<string>(["h264"]), CancellationToken.None);

        // Computed should be invalidated
        computed.IsConsistent().Should().BeFalse("registering member should invalidate codec computed");

        // Updated value should reflect limited codecs
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().Contain("h264");
        // av1 should NOT be in the list since the only member doesn't support it
        computed.Value.Should().NotContain("av1",
            "av1 should be removed when a member doesn't support it");
    }

    // --- Redis data survives shard-switch simulation ---

    [Fact]
    public async Task AudioBackend_RedisDataSurvivesCacheInvalidation()
    {
        var (chatId, liveBackend) = await CreateChatWithAudioBackend("AudioSurvives");
        var streamInfo = NewAudioStreamInfo(chatId);

        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Simulate shard switch: invalidate all compute methods
        using (Invalidation.Begin())
            _ = liveBackend.List(chatId, default);

        // Redis should still have the data
        var state = await ReadRedisValue<LiveAudioBackend.State>("live-audio:state", chatId);
        state.Should().NotBeNull();
        state!.Streams.Select(s => s.StreamId).Should().Contain(streamInfo.StreamId,
            "Redis data should NOT be purged on shard switch");

        // List() should recover from Redis
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle().Which.StreamId.Should().Be(streamInfo.StreamId);
    }

    [Fact]
    public async Task VideoBackend_RedisDataSurvivesCacheInvalidation()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("VideoSurvives");

        // Register a stream
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(streamId, chatId, AuthorId.New(chatId, 1),
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now);
        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Register a member
        var sessionId = $"session-{Guid.NewGuid():N}";
        await liveBackend.RegisterMember(chatId, sessionId,
            new ApiArray<string>(["h264", "av1"]), CancellationToken.None);

        // Simulate shard switch: invalidate ALL compute methods
        using (Invalidation.Begin()) {
            _ = liveBackend.List(chatId, default);
            _ = liveBackend.GetVideoStreamMemberCount(chatId, default);
            _ = liveBackend.GetSupportedCodecs(chatId, default);
        }

        // Redis should still have both streams and members
        var redisStreams = await ReadRedisHash<VideoStreamInfo>("live-video:streams", chatId);
        redisStreams.Should().ContainKey(streamId.Value,
            "stream data should survive shard switch");

        var redisMembers = await ReadRedisHash<VideoStreamMemberInfo>("live-video:members", chatId);
        redisMembers.Should().ContainKey(sessionId,
            "member data should survive shard switch");

        // All computed methods should recover from Redis
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        streams.Should().ContainSingle();

        var memberCount = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        memberCount.Should().Be(1);

        var codecs = await liveBackend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().Contain("h264");
    }

    // --- VP9 codec negotiation ---

    [Fact]
    public async Task VideoBackend_ShouldNegotiateVp9WhenAllMembersSupport()
    {
        var (chatId, liveBackend) = await CreateChatWithVideoBackend("Vp9Negotiate");

        // Member A supports everything
        await liveBackend.RegisterMember(chatId, $"session-{Guid.NewGuid():N}",
            new ApiArray<string>(["av1", "hevc", "vp9", "h264"]), CancellationToken.None);

        // Member B only supports vp9 + h264
        await liveBackend.RegisterMember(chatId, $"session-{Guid.NewGuid():N}",
            new ApiArray<string>(["vp9", "h264"]), CancellationToken.None);

        var codecs = await liveBackend.GetSupportedCodecs(chatId, CancellationToken.None);
        codecs.Should().BeEquivalentTo(new[] { "vp9", "h264" },
            o => o.WithStrictOrdering(),
            "vp9 should be recommended when all members support it but not av1/hevc");
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

    private LiveAudioStreamInfo NewAudioStreamInfo(ChatId chatId)
        => new() {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

    private VideoStreamInfo NewVideoStreamInfo(ChatId chatId, AuthorId authorId, VideoSourceKind sourceKind)
        => new(
            StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref),
            chatId,
            authorId,
            new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) },
            Clocks.SystemClock.Now,
            sourceKind);

    private async Task<Dictionary<string, TValue>> ReadRedisHash<TValue>(string keyPrefix, ChatId chatId)
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var key = $"{keyPrefix}{RedisDb.KeyDelimiter}{chatId}";
        var entries = await db.HashGetAllAsync(key).ConfigureAwait(false);
        var result = new Dictionary<string, TValue>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries) {
            var field = entry.Name.ToString();
            var value = (TValue?)RedisHashStoreSerializer.Read((byte[])entry.Value!, typeof(TValue), out _);
            if (value != null)
                result[field] = value;
        }
        return result;
    }

    private async Task<TValue?> ReadRedisValue<TValue>(string keyPrefix, ChatId chatId)
        where TValue : class
    {
        var db = await RedisDb.Database.Get().ConfigureAwait(false);
        var key = $"{keyPrefix}{RedisDb.KeyDelimiter}{chatId}";
        var raw = await db.StringGetAsync(key).ConfigureAwait(false);
        if (raw.IsNullOrEmpty)
            return null;
        return (TValue?)RedisHashStoreSerializer.Read((byte[])raw!, typeof(TValue), out _);
    }
}
