using System.Threading.Channels;
using BenchmarkDotNet.Attributes;


namespace ActualChat.Core.Benchmarks;

/// <summary>
/// Benchmarks for AsyncMemoizer per-frame allocation and throughput.
/// Run: dotnet run -c Release --project tests/Core.Benchmarks
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob]
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
        var memoizer = source.Memoize(capacity);

        // Start consumers
        var consumers = new Task[ConsumerCount];
        for (var i = 0; i < ConsumerCount; i++)
            consumers[i] = Task.Run(async () => {
                await foreach (var _ in memoizer.Replay(90)) { }
            });

        // Give consumers time to register
        if (ConsumerCount > 0)
            await Task.Delay(10);

        // Produce frames
        for (var i = 0; i < frameCount; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        if (ConsumerCount > 0)
            await Task.WhenAll(consumers);
        else
            await memoizer.WriteTask;

        memoizer.Dispose();
    }

    [Benchmark]
    public async Task Produce10K_Unbounded()
    {
        const int frameCount = 10_000;

        var source = Channel.CreateUnbounded<int>();
        var memoizer = source.Memoize();

        // Start consumers
        var consumers = new Task[ConsumerCount];
        for (var i = 0; i < ConsumerCount; i++)
            consumers[i] = Task.Run(async () => {
                await foreach (var _ in memoizer.Replay()) { }
            });

        if (ConsumerCount > 0)
            await Task.Delay(10);

        for (var i = 0; i < frameCount; i++)
            source.Writer.TryWrite(i);
        source.Writer.Complete();

        if (ConsumerCount > 0)
            await Task.WhenAll(consumers);
        else
            await memoizer.WriteTask;

        memoizer.Dispose();
    }
}
