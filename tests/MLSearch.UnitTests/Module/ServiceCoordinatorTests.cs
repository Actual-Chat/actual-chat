using ActualChat.MLSearch.Engine.OpenSearch.Setup;
using ActualChat.MLSearch.Module;
using ActualLab.Resilience;

namespace ActualChat.MLSearch.UnitTests.Module;

public class ServiceCoordinatorTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task RunOfServiceCoordinatorStartsInitialization()
    {
        var clusterSetup = new Mock<IClusterSetup>();
        clusterSetup
            .Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceCoordinator = new ServiceCoordinator(
            clusterSetup.Object,
            Mock.Of<IMomentClock>(),
            Mock.Of<ILogger<ServiceCoordinator>>());

        await serviceCoordinator.Run();

        clusterSetup.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CoordinatedServicesWaitForInitializationCompletion()
    {
        var serviceCoordinator = new ServiceCoordinator(
            Mock.Of<IClusterSetup>(),
            Mock.Of<IMomentClock>(),
            Mock.Of<ILogger<ServiceCoordinator>>());

        var dependentAction = serviceCoordinator.ExecuteWhenReadyAsync(_ => Task.CompletedTask, CancellationToken.None);
        var dependentFunc = serviceCoordinator.ExecuteWhenReadyAsync(_ => Task.FromResult(true), CancellationToken.None);
        Assert.False(dependentAction.IsCompleted);
        Assert.False(dependentFunc.IsCompleted);

        await serviceCoordinator.Run();

        // Both dependent routines must complete successfully
        await dependentAction;
        await dependentFunc;
    }

    [Fact]
    public async Task CoordinatorRetriesInitializationAtLeast1KTimes()
    {
        const int maxAttempts = 1_000;
        var errorTask = Task.FromException(new InvalidOperationException("Something is wrong."));
        var attemptCount = 0;
        var clusterSetup = new Mock<IClusterSetup>();
        clusterSetup
            .Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ => ++attemptCount < maxAttempts ? errorTask : Task.CompletedTask);

        var zeroRetryDelaySeq = new Mock<RetryDelaySeq>();
        zeroRetryDelaySeq.SetupGet(x => x[It.IsAny<int>()]).Returns(TimeSpan.Zero);

        // Quick check if sequence generates zero delays
        Assert.True(zeroRetryDelaySeq.Object.Take(10).All(delay => delay==TimeSpan.Zero));

        var serviceCoordinator = new ServiceCoordinator(
            clusterSetup.Object,
            Mock.Of<IMomentClock>(),
            Mock.Of<ILogger<ServiceCoordinator>>()) {
                RetryDelaySeq = zeroRetryDelaySeq.Object,
            };

        await serviceCoordinator.Run();

        clusterSetup.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Exactly(maxAttempts));
    }

    [Fact]
    public async Task InitializationSilentlyExitsOnCancellation()
    {
        var gate = new TaskCompletionSource();
        var neverEndingTask = ActualLab.Async.TaskExt.NewNeverEndingUnreferenced();
        OperationCanceledException? cancellationError = null;
        var clusterSetup = new Mock<IClusterSetup>();
        clusterSetup
            .Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct => {
                gate.SetResult();
                try {
                    await neverEndingTask.WaitAsync(ct);
                }
                catch (OperationCanceledException e) {
                    cancellationError = e;
                    throw;
                }
            });

        var serviceCoordinator = new ServiceCoordinator(
            clusterSetup.Object,
            Mock.Of<IMomentClock>(),
            Mock.Of<ILogger<ServiceCoordinator>>());

        var dependentAction = serviceCoordinator.ExecuteWhenReadyAsync(_ => Task.CompletedTask, CancellationToken.None);

        var runTask = serviceCoordinator.Run();
        await gate.Task;
        _ = serviceCoordinator.Stop();
        await runTask.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.NotNull(cancellationError);
        Assert.False(dependentAction.IsCompleted);
    }

    [Fact]
    public async Task InitializationDoesNotStartIfAlreadyCancelled()
    {
        var startCompletionSource = new TaskCompletionSource();
        var startSignal = new TaskCompletionSource();

        var clusterSetup = new Mock<IClusterSetup>();
        clusterSetup
            .Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceCoordinator = new ServiceCoordinator(
            clusterSetup.Object,
            Mock.Of<IMomentClock>(),
            Mock.Of<ILogger<ServiceCoordinator>>()) {
                OnStartTask = OnStart(),
            };

        var runTask = serviceCoordinator.Run();
        // Ensure we reached OnStart method
        await startSignal.Task;
        // Trigger cancellation
        _ = serviceCoordinator.Stop();
        // Complete OnStart
        startCompletionSource.SetResult();
        // Wait for completion
        await runTask;

        clusterSetup.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Never);
        return;

        async Task OnStart() {
            startSignal.SetResult();
            await startCompletionSource.Task;
        }
    }

    [Fact]
    public async Task CoordinatorRetriesInternalNonTerminalErrorsButExitsOnTerminalOnes()
    {
        var initializationError = Task.FromException(new ExternalError());
        var clusterSetup = new Mock<IClusterSetup>();
        clusterSetup
            .Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(initializationError);
        var clock = new Mock<IMomentClock>();
        clock.Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var errorRetryDelaySeq = new Mock<RetryDelaySeq>();
        errorRetryDelaySeq.SetupGet(x => x[It.IsAny<int>()]).Throws<InvalidOperationException>();

        var retryCount = 0;
        const int maxRetries = 10;
        var transiencies = new [] { Transiency.Unknown, Transiency.NonTransient, Transiency.Transient, Transiency.SuperTransient };

        var serviceCoordinator = new ServiceCoordinator(
            clusterSetup.Object,
            clock.Object,
            Mock.Of<ILogger<ServiceCoordinator>>()) {
                RetryDelaySeq = errorRetryDelaySeq.Object,
                TransiencyResolver = TransiencyResolver,
        };

        await serviceCoordinator.Run();


        clock.Verify(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(maxRetries));
        // As the first attempt isn't prepended by a retry, so:
        const int initializeCallCount = maxRetries + 1;
        clusterSetup.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Exactly(initializeCallCount));
        // Number of attempts to get a delay is equal to number of initialize failures
        errorRetryDelaySeq.VerifyGet(x => x[It.IsAny<int>()], Times.Exactly(initializeCallCount));

        return;

        Transiency TransiencyResolver(Exception e)
        {
            if (e is ExternalError) {
                return Transiency.Transient;
            }
            var transiency = retryCount < maxRetries
                ? transiencies[retryCount % transiencies.Length]
                : Transiency.Terminal;
            retryCount++;
            return transiency;
        }
    }
}
