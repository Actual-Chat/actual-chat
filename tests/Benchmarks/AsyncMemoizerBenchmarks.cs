using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace ActualChat.Benchmarks;

/// <summary>
/// Benchmarks for <see cref="AsyncMemoizer{T}"/> per-frame allocation and throughput
/// across various reader counts.
/// Run: dotnet run -c Release --project tests/Benchmarks
/// </summary>
[Config(typeof(InProcessShortRunConfig))]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class AsyncMemoizerBenchmarks
{
    [Params(0, 1, 10, 100)]
    public int ConsumerCount { get; set; }

    [Benchmark]
    public async Task Produce10K_Bounded()
    {
        const int frameCount = 10_000;
        const int capacity = 150; // matches video pipeline

        var source = Channel.CreateUnbounded<int>();
        var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync(), capacity);

        var consumers = new Task[ConsumerCount];
        for (var i = 0; i < ConsumerCount; i++)
            consumers[i] = Task.Run(async () => {
                await foreach (var _ in memoizer.Replay(90)) { }
            });

        for (var i = 0; i < frameCount; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        if (ConsumerCount > 0)
            await Task.WhenAll(consumers);

        await memoizer.DisposeAsync();
    }

    [Benchmark]
    public async Task Produce10K_Unbounded()
    {
        const int frameCount = 10_000;

        var source = Channel.CreateUnbounded<int>();
        var memoizer = new AsyncMemoizer<int>(source.Reader.ReadAllAsync());

        var consumers = new Task[ConsumerCount];
        for (var i = 0; i < ConsumerCount; i++)
            consumers[i] = Task.Run(async () => {
                await foreach (var _ in memoizer.Replay()) { }
            });

        for (var i = 0; i < frameCount; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        if (ConsumerCount > 0)
            await Task.WhenAll(consumers);

        await memoizer.DisposeAsync();
    }
}
