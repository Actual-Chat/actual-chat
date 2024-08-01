namespace ActualChat.Jobs;

internal sealed class JobsRunner(
    IServiceProvider services,
    IEnumerable<IJobMetadata> jobsMetadata,
    MomentClockSet clocks) : IJobsRunner
{
    private readonly ILogger<JobsRunner> _logger = services.LogFor<JobsRunner>();

    public async Task Start(CancellationToken token)
    {
        _logger.LogInformation("Jobs runner is starting");

        var tasks = jobsMetadata
            .Select(jobMetadata => RunJob(jobMetadata, token))
            .ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogInformation("Jobs runner is stopped");
    }

    private async Task RunJob(IJobMetadata jobMetadata, CancellationToken token)
    {
        _logger.LogInformation("{Job} job is starting", jobMetadata.Name);

        using var retryDelays = RetryDelaySeq.Exp(30, 600).Delays().GetEnumerator();
        var shouldExecute = jobMetadata.ExecuteAtStart;

        while (!token.IsCancellationRequested) {
            try {
                if (shouldExecute) {
                    using var scope = services.CreateScope();
                    var job = scope.ServiceProvider.GetRequiredService(jobMetadata.JobType);

                    _logger.LogInformation("{Job} job is executing", jobMetadata.Name);
                    await ((IJob)job).Run(clocks.SystemClock.UtcNow, token).ConfigureAwait(false);
                    _logger.LogInformation("{Job} job is executed", jobMetadata.Name);
                }
                else {
                    shouldExecute = true;
                }

                var now = clocks.SystemClock.UtcNow;
                var next = jobMetadata.GetNextExecutionTime(now);
                if (next <= now)
                    break;

                var delay = next - now;

                _logger.LogInformation(
                    "Execution of {Job} job is scheduled on {Date} at {Time} UTC (in {Delay})",
                    jobMetadata.Name,
                    next.ToInvariantString("yyyy-MM-dd"),
                    next.TimeOfDay,
                    delay);

                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == token) {
                // noop
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Job '{Job}' failed", jobMetadata.Name);
                retryDelays.MoveNext();
                await Task.Delay(retryDelays.Current, token).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("{Job} job is stopped", jobMetadata.Name);
    }
}
