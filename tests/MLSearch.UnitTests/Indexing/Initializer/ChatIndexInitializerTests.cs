using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.MLSearch.ApiAdapters.ShardWorker;
using ActualChat.MLSearch.Indexing.Initializer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.MLSearch.UnitTests.Indexing.Initializer;

public class ChatIndexInitializerTests(ITestOutputHelper @out) : TestBase(@out)
{
    private const int InactiveShardIndex1 = 1;
    private const int ActiveShardIndex = 2;
    private const int InactiveShardIndex2 = 3;

    private static readonly MethodInfo _onRun = typeof(ChatIndexInitializer).GetMethod(
        "OnRun",
        BindingFlags.Instance | BindingFlags.NonPublic,
        Type.DefaultBinder,
        new[] {typeof (int), typeof (CancellationToken)},
        null)!;

    [Fact]
    public async Task ShardIndexResolverReceivesExpectedValues()
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = new Mock<IShardIndexResolver<string>>();
        shardIndexResolver
            .Setup(x => x.Resolve(It.IsAny<IHasShardKey<string>>(), It.IsAny<ShardScheme>()))
            .Returns(ActiveShardIndex)
            .Verifiable();
        var shard = Mock.Of<IChatIndexInitializerShard>();
        var logger = Mock.Of<ILogger<ChatIndexInitializer>>();
        await using var initializer = new ChatIndexInitializer(services, scheme, shardIndexResolver.Object, shard, logger);

        // Trigger shard index evaluation
        _onRun.Invoke(initializer, [InactiveShardIndex1, CancellationToken.None]);

        shardIndexResolver
            .Verify(resolver => resolver.Resolve(
                It.Is<IHasShardKey<string>>(evt => evt.ShardKey==ChatIndexInitializerShardKey.Value),
                It.Is<ShardScheme>(s => ReferenceEquals(s, scheme))
            ), Times.Once());
    }

    [Fact]
    public async Task PostAsyncThrowsErrorIfNoShardsStarted()
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = Mock.Of<IShardIndexResolver<string>>();
        var shard = Mock.Of<IChatIndexInitializerShard>();
        var logger = Mock.Of<ILogger<ChatIndexInitializer>>();
        await using var initializer = new ChatIndexInitializer(services, scheme, shardIndexResolver, shard, logger);
        await Assert.ThrowsAsync<NotFoundException<ChatIndexInitializerShard>>(
            async () => await initializer.PostAsync(
                new MLSearch_TriggerChatIndexingCompletion(ChatId.None), CancellationToken.None));
    }

    [Fact]
    public async Task PostAsyncThrowsErrorIfNoActiveShardFound()
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = new Mock<IShardIndexResolver<string>>();
        shardIndexResolver
            .Setup(x => x.Resolve(It.IsAny<IHasShardKey<string>>(), It.IsAny<ShardScheme>()))
            .Returns(ActiveShardIndex);
        var shard = Mock.Of<IChatIndexInitializerShard>();
        var logger = Mock.Of<ILogger<ChatIndexInitializer>>();
        await using var initializer = new ChatIndexInitializer(services, scheme, shardIndexResolver.Object, shard, logger);
        // Emulate staring of some inactive shards
        _onRun.Invoke(initializer, [InactiveShardIndex1, CancellationToken.None]);
        _onRun.Invoke(initializer, [InactiveShardIndex2, CancellationToken.None]);
        await Assert.ThrowsAsync<NotFoundException<ChatIndexInitializerShard>>(
            async () => await initializer.PostAsync(
                new MLSearch_TriggerChatIndexingCompletion(ChatId.None), CancellationToken.None));
    }

    [Theory]
    [InlineData(new[] {ActiveShardIndex})]
    [InlineData(new[] {InactiveShardIndex1, ActiveShardIndex, InactiveShardIndex2})]
    public async Task PostAsyncPropagatesEventAndCancellationTokenIfActiveShardFound(int[] shardIds)
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = new Mock<IShardIndexResolver<string>>();
        shardIndexResolver
            .Setup(x => x.Resolve(
                It.IsAny<IHasShardKey<string>>(),
                It.IsAny<ShardScheme>()))
            .Returns(ActiveShardIndex);
        var shard = new Mock<IChatIndexInitializerShard>();
        shard
            .Setup(x => x.PostAsync(
                It.IsAny<MLSearch_TriggerChatIndexingCompletion>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask)
            .Verifiable();
        var logger = Mock.Of<ILogger<ChatIndexInitializer>>();
        await using var initializer =
            new ChatIndexInitializer(services, scheme, shardIndexResolver.Object, shard.Object, logger);
        // Emulate staring of inactive & active shards
        foreach (var shardId in shardIds) {
            _onRun.Invoke(initializer, [shardId, CancellationToken.None]);
        }

        var completionEvt = new MLSearch_TriggerChatIndexingCompletion(new ChatId(Generate.Option));
        var cancellationToken = new CancellationTokenSource().Token;

        await initializer.PostAsync(completionEvt, cancellationToken);

        shard.Verify(ms => ms.PostAsync(
            It.Is<MLSearch_TriggerChatIndexingCompletion>(evt => evt==completionEvt),
            It.Is<CancellationToken>(token => token==cancellationToken)
        ), Times.Once());
    }

    [Fact]
    public async Task OnRunOfActiveShardLogInformationAndStartUsingIt()
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = new Mock<IShardIndexResolver<string>>();
        shardIndexResolver
            .Setup(x => x.Resolve(It.IsAny<IHasShardKey<string>>(),It.IsAny<ShardScheme>()))
            .Returns(ActiveShardIndex);
        var shard = new Mock<IChatIndexInitializerShard>();
        shard
            .Setup(x => x.UseAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        var logger = new Mock<ILogger<ChatIndexInitializer>>();
        logger
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Verifiable();
        await using var initializer =
            new ChatIndexInitializer(services, scheme, shardIndexResolver.Object, shard.Object, logger.Object);

        var cancellationToken = new CancellationTokenSource().Token;
        _onRun.Invoke(initializer, [ActiveShardIndex, cancellationToken]);
        shard.Verify(sh => sh.UseAsync(
                It.Is<CancellationToken>(token => token == cancellationToken)
            ), Times.Once());
        logger.Verify(log => log.Log(
                It.Is<LogLevel>(lvl => lvl==LogLevel.Information),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()
            ), Times.Once());
    }

    [Fact]
    public async Task OnRunOfInactiveShardReturnsNotCompletedButCancellableTask()
    {
        var services = MoqServiceProvider().Object;
        var scheme = new ShardScheme("TestScheme", 2, HostRole.OneServer);
        var shardIndexResolver = new Mock<IShardIndexResolver<string>>();
        shardIndexResolver
            .Setup(x => x.Resolve(It.IsAny<IHasShardKey<string>>(), It.IsAny<ShardScheme>()))
            .Returns(ActiveShardIndex);
        var shard = Mock.Of<IChatIndexInitializerShard>();
        var logger = Mock.Of<ILogger<ChatIndexInitializer>>();
        await using var initializer = new ChatIndexInitializer(services, scheme, shardIndexResolver.Object, shard, logger);

        var cancellationTokenSource = new CancellationTokenSource();
        // Emulate staring of an inactive shard
        var onRunTask = (Task)_onRun.Invoke(initializer, [InactiveShardIndex1, cancellationTokenSource.Token])!;
        Assert.False(onRunTask.IsCompleted);
        Assert.False(onRunTask.IsCanceled);
        Assert.False(onRunTask.IsFaulted);
        await cancellationTokenSource.CancelAsync();
        Assert.True(onRunTask.IsCanceled);
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await onRunTask);
    }

    private static Mock<IServiceProvider> MoqServiceProvider()
    {
        var moqServices = new Mock<IServiceProvider> { DefaultValueProvider = DefaultValueProvider.Mock };
        moqServices
            .Setup(x => x.GetService(typeof(ILoggerFactory)))
            .Returns(NullLoggerFactory.Instance);
        moqServices
            .Setup(x => x.GetService(typeof(IHostApplicationLifetime)))
            .Returns(Mock.Of<IHostApplicationLifetime>());
        var moqMeshLocks = new Mock<IMeshLocks<InfrastructureDbContext>>();
        moqMeshLocks
            .Setup(x => x.With(It.IsAny<string>(), It.IsAny<MeshLockOptions?>()))
            .Returns(moqMeshLocks.Object);
        moqServices
            .Setup(x => x.GetService(typeof(IMeshLocks<InfrastructureDbContext>)))
            .Returns(moqMeshLocks.Object);
        moqServices
            .Setup(x => x.GetService(typeof(IStateFactory)))
            .Returns(Mock.Of<IStateFactory>());
        moqServices
            .Setup(x => x.GetService(typeof(HostInfo)))
            .Returns(() => new HostInfo());
        moqServices
            .Setup(x => x.GetService(typeof(MeshWatcher)))
            .Returns(() => new MeshWatcher(moqServices.Object, false));
        moqServices
            .Setup(x => x.GetService(typeof(MeshNode)))
            .Returns(() => new MeshNode(NodeRef.None, string.Empty, new ApiSet<HostRole>()));
        return moqServices;
    }
}
