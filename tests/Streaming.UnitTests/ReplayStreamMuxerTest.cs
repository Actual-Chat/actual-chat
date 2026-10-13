using ActualChat.Chat;
using ActualChat.Streaming.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ActualChat.Streaming.UnitTests;

public class ReplayStreamMuxerTest
{
    [Fact]
    public async Task AFailedReplayShouldReachTheListenerAsAnError()
    {
        // arrange
        var chats = new Mock<IChats>(MockBehavior.Loose);
        chats
            .Setup(x => x.Get(It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Chat storage is down"));
        var services = new ServiceCollection()
            .AddSingleton(chats.Object)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        await using var muxer = new ReplayStreamMuxer(
            services, Session.New(), ChatId.Parse("the-actual-one"), Moment.EpochStart, TimeSpan.Zero);

        // act
        var read = async () => {
            await foreach (var _ in muxer.Output.ReadAllAsync().ConfigureAwait(false)) { }
        };

        // assert
        await read.Should().ThrowAsync<InvalidOperationException>().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
