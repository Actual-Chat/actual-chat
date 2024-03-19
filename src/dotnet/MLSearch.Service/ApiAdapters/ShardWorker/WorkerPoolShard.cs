
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerPoolShard<TWorker, TJob, TJobId, TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    ValueTask PostAsync(TJob job, CancellationToken cancellationToken);
    Task UseAsync(CancellationToken cancellationToken);
}

internal class WorkerPoolShard<TWorker, TJob, TJobId, TShardKey>(
    int shardIndex,
    DuplicateJobPolicy duplicateJobPolicy,
    int concurrencyLevel,
    TWorker worker,
    ILogger<TWorker> log
) : IWorkerPoolShard<TWorker, TJob, TJobId, TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private record class RunningJob(Task Completion, CancellationTokenSource Cancellation);

    private readonly Channel<TJob> _jobBuffer =
        Channel.CreateBounded<TJob>(new BoundedChannelOptions(concurrencyLevel) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<TJobId, RunningJob?> _runningJobs = new(-1, concurrencyLevel);
    private readonly SemaphoreSlim _semaphore = new(concurrencyLevel, concurrencyLevel);

    public int ShardIndex => shardIndex;
    public DuplicateJobPolicy DuplicateJobPolicy => duplicateJobPolicy;

    public async ValueTask PostAsync(TJob job, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _jobBuffer.Writer.WriteAsync(job, cancellationToken).ConfigureAwait(false);
    }

    public async Task UseAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var job = await _jobBuffer.Reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (_runningJobs.TryAdd(job.Id, null)) {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    _runningJobs[job.Id] = new(
                        RunJobAsync(job, cancellation.Token),
                        cancellation);
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
        var completions = new List<Task>(concurrencyLevel);
        var jobs = _runningJobs.Values.Where(job => job is not null).Select(job => job!);
        foreach (var (completion, cancellation) in jobs) {
            cancellation.DisposeSilently();
            completions.Add(completion);
        }
        using var delayCancellation = new CancellationTokenSource();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(10), delayCancellation.Token);
        if (delayTask != await Task.WhenAny(Task.WhenAll(completions), delayTask).ConfigureAwait(false)) {
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task RunJobAsync(TJob job, CancellationToken cancellationToken)
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
            jobName, job.Id, shardIndex);
        try {
            await worker.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
            log.LogInformation("Job {JobType} #{JobId} at shard #{ShardIndex} is completed.",
                jobName, job.Id, shardIndex);
        }
        catch (OperationCanceledException) {
            log.LogInformation("Job {JobType} #{JobId} at shard #{ShardIndex} is cancelled.",
                jobName, job.Id, shardIndex);
        }
        catch (Exception e) {
            log.LogError(e, "Job {JobType} #{JobId} at shard #{ShardIndex} is failed.",
                jobName, job.Id, shardIndex);
        }
        finally {
            if (_runningJobs.TryRemove(job.Id, out var runningJob)) {
                runningJob?.Cancellation.DisposeSilently();
            }
            _semaphore.Release();
        }
    }
}
