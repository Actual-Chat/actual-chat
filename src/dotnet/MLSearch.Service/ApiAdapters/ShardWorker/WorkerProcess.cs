
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerProcess<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    ValueTask PostAsync(TCommand input, CancellationToken cancellationToken);
    Task RunAsync(CancellationToken cancellationToken);
}

internal class WorkerProcess<TWorker, TCommand, TJobId, TShardKey>(
    int shardIndex,
    DuplicateJobPolicy duplicateJobPolicy,
    int concurrencyLevel,
    TWorker worker,
    ILogger<TWorker> log
) : IWorkerProcess<TWorker, TCommand, TJobId, TShardKey>
    where TWorker : class, IWorker<TCommand>
    where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private record class Job(Task Completion, CancellationTokenSource Cancellation);

    private readonly Channel<TCommand> _channel =
        Channel.CreateBounded<TCommand>(new BoundedChannelOptions(concurrencyLevel) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<TJobId, Job?> _runningJobs = new(-1, concurrencyLevel);
    private readonly SemaphoreSlim _semaphore = new(concurrencyLevel, concurrencyLevel);

    public int ShardIndex => shardIndex;
    public DuplicateJobPolicy DuplicateJobPolicy => duplicateJobPolicy;

    public async ValueTask PostAsync(TCommand input, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _channel.Writer.WriteAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var input = await _channel
                    .Reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (_runningJobs.TryAdd(input.Id, null)) {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var completion = StartAsync(input, cancellation.Token);
                    _runningJobs[input.Id] = new Job(completion, cancellation);
                }
                else {
                    _semaphore.Release();
                }
            }
            catch (Exception e){
                log.LogError(e.Message);
                throw;
            }
        }
        var indexingTasks = new List<Task>(concurrencyLevel);
        var jobs = _runningJobs.Values.Where(job => job is not null).Select(job => job!);
        foreach (var (completion, cancellation) in jobs) {
            cancellation.DisposeSilently();
            indexingTasks.Add(completion);
        }
        using var delayCancellation = new CancellationTokenSource();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(10), delayCancellation.Token);
        if (delayTask != await Task.WhenAny(Task.WhenAll(indexingTasks), delayTask).ConfigureAwait(false)) {
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task StartAsync(TCommand input, CancellationToken cancellationToken)
    {
        await Task.Yield();

        // This method is a single unit of work.
        // As far as I understood there's an embedded assumption made
        // that it is possible to rehash shards attached to the host
        // between OnRun method executions.
        //
        // We calculate stream cursor each call to prevent
        // issues in case of re-sharding or new cluster rollouts.
        // It might have some other concurrent worker has updated
        // a cursor. TLDR: prevent stale cursor data.

        var jobName = worker.GetType().Name;
        log.LogInformation("Starting job {JobType} #{JobId} at shard #{ShardIndex}.",
            jobName, input.Id, shardIndex);
        try {
            await worker.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
            log.LogInformation("Job {JobType} #{JobId} at shard #{ShardIndex} is completed.",
                jobName, input.Id, shardIndex);
        }
        catch (OperationCanceledException) {
            log.LogInformation("Job {JobType} #{JobId} at shard #{ShardIndex} is cancelled.",
                jobName, input.Id, shardIndex);
        }
        catch (Exception e) {
            log.LogError(e, "Job {JobType} #{JobId} at shard #{ShardIndex} is failed.",
                jobName, input.Id, shardIndex);
        }
        finally {
            if (_runningJobs.TryRemove(input.Id, out var job)) {
                job?.Cancellation.DisposeSilently();
            }
            _semaphore.Release();
        }
    }
}
