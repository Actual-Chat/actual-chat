
namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

// Note:
// This is super confusing. The scheme name MUST be a role name.
// It is getting a scheme from the role of the host. Super-super confusing.
// And the immediate next problem: Shard worker is expecting to accept a sharding scheme.
// This makes me think that I can create different schemes for different shard workers.
// However since it applies on the role level it's not a correct expectation.
// In order to solve this. I would suggest to:
// - Very Explicitly register a shard scheme against a role.
// - modify how this workers are getting registered:
//   services.AddWorker<HostRole, TWorker / TShardWorker>
// -

internal class ShardWorkerFunc<TName>(
    IServiceProvider services,
    ShardScheme shardScheme,
    Func<int, CancellationToken, Task> run
) : ActualChat.ShardWorker(services, shardScheme, typeof(TName).Name)
{
    protected override Task OnRun(int shardIndex, CancellationToken cancellationToken)
        => run(shardIndex, cancellationToken);
}


internal interface IShardCommandDispatcher<TCommand>
{
    ValueTask DispatchAsync(TCommand input, CancellationToken cancellationToken);
}

internal class ShardCommandWorker<TWorker, TCommand, TJobId, TShardKey>(
    IServiceProvider services,
    ShardScheme shardScheme,
    DuplicateJobPolicy duplicateJobPolicy,
    IShardIndexResolver<TShardKey> shardIndexResolver,
    IShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey> workerFactory
) : ActualChat.ShardWorker(services, shardScheme, typeof(TWorker).Name), IShardCommandDispatcher<TCommand>
    where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TWorker : IWorker<TCommand>
{
    private readonly ConcurrentDictionary<int, IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>> _workerProcesses = new();

    public async ValueTask DispatchAsync(TCommand input, CancellationToken cancellationToken)
    {
        var shardIndex = shardIndexResolver.Resolve(input, ShardScheme);
        if (!_workerProcesses.TryGetValue(shardIndex, out var workerProcess)) {
            throw StandardError.NotFound<TWorker>(
                $"{nameof(TWorker)} instance for the shard #{shardIndex.Format()} is not found.");
        }
        await workerProcess.PostAsync(input, cancellationToken).ConfigureAwait(false);
    }
    protected async override Task OnRun(int shardIndex, CancellationToken cancellationToken)
    {
        var workerProcess = _workerProcesses.AddOrUpdate(shardIndex,
            (key, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy),
            (key, _, arg) => arg.Factory.Create(key, arg.DuplicateJobPolicy),
            (Factory: workerFactory, DuplicateJobPolicy: duplicateJobPolicy));
        try {
            await workerProcess.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            // Clean up dictionary of workers if it still contains worker being stopped
            _workerProcesses.TryRemove(new KeyValuePair<int, IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>>(shardIndex, workerProcess));
        }
    }
}

internal interface IWorker<TCommand>
{
    Task ExecuteAsync(TCommand input, CancellationToken cancellationToken);
}

internal interface IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>
    where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TWorker : IWorker<TCommand>
{
    ValueTask PostAsync(TCommand input, CancellationToken cancellationToken);
    Task RunAsync(CancellationToken cancellationToken);
}

internal class ShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>(
    int shardIndex,
    DuplicateJobPolicy duplicateJobPolicy,
    TWorker worker,
    ILogger<TWorker> log
) : IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>
    where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TWorker : IWorker<TCommand>
{
    private record class Job(Task Completion, CancellationTokenSource Cancellation);
    private const int ChannelCapacity = 10;

    private readonly Channel<TCommand> _channel =
        Channel.CreateBounded<TCommand>(new BoundedChannelOptions(ChannelCapacity) {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly ConcurrentDictionary<TJobId, Job?> _runningJobs = new(-1, ChannelCapacity);
    private readonly SemaphoreSlim _semaphore = new(ChannelCapacity, ChannelCapacity);

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


internal enum DuplicateJobPolicy
{
    Drop,
    Run,
    Cancel,
}

internal interface IShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TWorker : IWorker<TCommand>
{
    IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy);
}

internal class ShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>(IServiceProvider services)
    : IShardWorkerProcessFactory<TWorker, TCommand, TJobId, TShardKey>
    where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    where TWorker : IWorker<TCommand>
{
    private readonly ObjectFactory<ShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>> factoryMethod =
        ActivatorUtilities.CreateFactory<ShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey>>([typeof(int), typeof(DuplicateJobPolicy)]);

    public IShardWorkerProcess<TWorker, TCommand, TJobId, TShardKey> Create(int shardIndex, DuplicateJobPolicy duplicateJobPolicy)
        => factoryMethod(services, [shardIndex, duplicateJobPolicy]);
}
