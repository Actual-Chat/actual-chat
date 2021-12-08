using Stl.Redis;

namespace ActualChat.Redis.IntegrationTests;

public class RedisStreamerTest : RedisTestBase
{
    public RedisStreamerTest(TestSettings settings) : base(settings)
    { }

    [Fact]
    public async Task DequeTest()
    {
        var db = GetRedisDb().WithKeyPrefix("source-audio");
        var queue = db.GetQueue("new-records",
            new RedisQueue<string>.Options() {
                DequeueTimeout = TimeSpan.FromMinutes(10),
            });

        // var dequeTask = Deque();
        var dequeTask = queue.EnqueuePubSub.Read();
        var streamer = db.GetStreamer<string>("1234567890");
        await streamer.Write(
            GetStringStream(),
            _ => queue.Enqueue("new stream message").ToValueTask());
        var queueValue = await dequeTask;

        async IAsyncEnumerable<string> GetStringStream()
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
            yield return "one";
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            yield return "two";
        }

        // queue.EnqueuePubSub.

        Task<string> Deque()
        {
            return queue.Dequeue();
        }
    }
}
