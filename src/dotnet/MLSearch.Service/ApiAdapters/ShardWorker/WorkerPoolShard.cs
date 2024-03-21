
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal interface IWorkerPoolShard<in TJob, in TJobId, in TShardKey>
    where TJob : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    ValueTask PostAsync(TJob job, CancellationToken cancellationToken);
    ValueTask CancelAsync(TJobId jobId, CancellationToken cancellationToken);
    Task UseAsync(CancellationToken cancellationToken);
}

internal class WorkerPoolShard<TWorker, TJob, TJobId, TShardKey>(
    int shardIndex,
    DuplicateJobPolicy duplicateJobPolicy,
    int concurrencyLevel,
    TWorker worker,
    ILogger<TWorker> log
) : IWorkerPoolShard<TJob, TJobId, TShardKey>
    where TWorker : class, IWorker<TJob>
    where TJob : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TJobId : notnull
    where TShardKey : notnull
{
    private record RunningJob(Task CompletionTask, CancellationTokenSource Cancellation, Task? CancellationTask = null);

    private record Assignment {
        private Assignment() { }

        public record RunJob(TJob Job) : Assignment;
        public record CancelJob(TJobId JobId) : Assignment;
    }

    private readonly string jobName = worker.GetType().Name;
    private readonly Channel<Assignment> _assignments =
        Channel.CreateBounded<Assignment>(new BoundedChannelOptions(concurrencyLevel+1) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<TJobId, RunningJob?> _runningJobs = new(-1, concurrencyLevel);
    private readonly SemaphoreSlim _semaphore = new(concurrencyLevel, concurrencyLevel);
    private readonly SemaphoreSlim _cancellationSemaphore = new(1, 1);

    public int ShardIndex => shardIndex;
    public DuplicateJobPolicy DuplicateJobPolicy => duplicateJobPolicy;

    public async ValueTask PostAsync(TJob job, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _assignments.Writer.WriteAsync(new Assignment.RunJob(job), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CancelAsync(TJobId jobId, CancellationToken cancellationToken)
    {
        await _cancellationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _assignments.Writer.WriteAsync(new Assignment.CancelJob(jobId), cancellationToken).ConfigureAwait(false);
    }

    public async Task UseAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var assignment = await _assignments.Reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                switch (assignment) {
                    case Assignment.RunJob(var job): {
                        if (duplicateJobPolicy==DuplicateJobPolicy.Drop) {
                            if (_runningJobs.TryAdd(job.Id, null)) {
                                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                _runningJobs[job.Id] = new RunningJob(
                                    RunJobAsync(job, cancellation.Token),
                                    cancellation);
                            }
                            else {
                                _semaphore.Release();
                            }
                        }
                        if (duplicateJobPolicy==DuplicateJobPolicy.Cancel) {
                            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var completionTask = _runningJobs.TryRemove(job.Id, out var runningJob)
                                ? CancelAndRunJobAsync(job, runningJob!, cancellation.Token)
                                : RunJobAsync(job, cancellation.Token);

                            _runningJobs[job.Id] = new RunningJob(completionTask, cancellation);
                        }
                    }
                    break;
                    case Assignment.CancelJob(var jobId): {
                        if (_runningJobs.TryGetValue(jobId, out var runningJob)) {
                            // runningJob can't be null here because if it is in the dictionary
                            // then previous iteration of the outer cycle is completely done
                            if (runningJob!.CancellationTask is null) {
                                _runningJobs[jobId] = runningJob with {
                                    CancellationTask = runningJob.Cancellation.CancelAsync()
                                };
                            }
                        }
                        _cancellationSemaphore.Release();
                    }
                    break;
                }
            }
            catch (Exception e){
                log.LogError(e.Message);
                throw;
            }
        }
        var completions = new List<Task>(concurrencyLevel);
        var jobs = _runningJobs.Values.Where(job => job is not null).Select(job => job!);
        foreach (var (completionTask, cancellation, _) in jobs) {
            cancellation.DisposeSilently();
            completions.Add(completionTask);
        }
        using var delayCancellation = new CancellationTokenSource();
        var delayTask = Task.Delay(TimeSpan.FromSeconds(10), delayCancellation.Token);
        if (delayTask != await Task.WhenAny(Task.WhenAll(completions), delayTask).ConfigureAwait(false)) {
            await delayCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task CancelAndRunJobAsync(TJob job, RunningJob runningJob, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try {
            var cancellationSource = runningJob.Cancellation;
            if (!cancellationSource.IsCancellationRequested && !cancellationSource.IsDisposed()) {
                await runningJob.Cancellation.CancelAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e) {
            log.LogError(e, "Unexpected error on cancelling {JobType} #{JobId} at shard #{ShardIndex}.",
                jobName, job.Id, shardIndex);
        }
        finally {
            await RunJobAsync(job, cancellationToken).ConfigureAwait(false);
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

        try {
            log.LogInformation("Starting job {JobType} #{JobId} at shard #{ShardIndex}.",
                jobName, job.Id, shardIndex);

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
