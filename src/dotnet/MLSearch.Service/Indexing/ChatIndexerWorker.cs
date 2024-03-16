namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexerWorker
{
    ValueTask PostAsync(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken);
    Task ExecuteAsync(CancellationToken cancellationToken);
}

internal class ChatIndexerWorker(
    int shardIndex,
    IDataIndexer<ChatId> dataIndexer,
    ILogger<ChatIndexerWorker> log
) : IChatIndexerWorker
{
    private record class Job(Task Completion, CancellationTokenSource Cancellation);
    private const int ChannelCapacity = 10;

    private readonly Channel<MLSearch_TriggerChatIndexing> _channel =
        Channel.CreateBounded<MLSearch_TriggerChatIndexing>(new BoundedChannelOptions(ChannelCapacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<ChatId, Job?> _runningJobs = new(-1, ChannelCapacity);
    private readonly SemaphoreSlim _semaphore = new(ChannelCapacity, ChannelCapacity);

    public int ShardIndex => shardIndex;

    public async ValueTask PostAsync(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        await _channel.Writer.WriteAsync(input, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                var input = await _channel
                    .Reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (_runningJobs.TryAdd(input.Id, null)) {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var completion = StartIndexing(input, cancellation.Token);
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
        var indexingTasks = new List<Task>(ChannelCapacity);
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

    private async Task StartIndexing(MLSearch_TriggerChatIndexing input, CancellationToken cancellationToken)
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

        var jobName = dataIndexer.GetType().Name;
        log.LogInformation("Starting job {JobType} #{JobId} at shard #{ShardIndex}.",
            jobName, input.Id, shardIndex);
        try {
            var continueIndexing = false;
            do {
                var result = await dataIndexer.IndexNextAsync(input.Id, cancellationToken).ConfigureAwait(false);
                continueIndexing = !(result.IsEndReached || cancellationToken.IsCancellationRequested);
            }
            while (continueIndexing);
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
