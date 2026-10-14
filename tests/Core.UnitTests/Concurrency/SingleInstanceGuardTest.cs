using ActualChat.Concurrency;
using ActualLab.Generators;
using ActualLab.IO;

namespace ActualChat.Core.UnitTests.Concurrency;

public class SingleInstanceGuardTest(ITestOutputHelper @out) : TestBase(@out), IDisposable
{
    private const string NamedEventsReason = "Named events are not supported on Unix";
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);
    private static TimeSpan WaitTimeout => TimeSpan.FromSeconds(10).CiScaled();

    private readonly List<TestGuard> _guards = new();
    private readonly string _name = $"Test-{RandomStringGenerator.Default.Next()}";
    private readonly string _directory = Directory.CreateTempSubdirectory("single-instance-").FullName;

    public void Dispose()
    {
        foreach (var guard in _guards) {
            guard.ReleaseExit.Set();
            guard.Dispose();
        }
        Directory.Delete(_directory, true);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task FirstGuardShouldTakeTheNameWithoutWaiting()
    {
        // arrange
        var guard = NewGuard();

        // act
        var isTaken = await TakeOver(guard);

        // assert
        isTaken.Should().BeTrue();
        guard.Outcome.Should().Be(SingleInstanceOutcome.NoOtherInstance);
        guard.Error.Should().BeNull();
        guard.ExitRequestCount.Should().Be(0);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task TakeOverShouldWritePidFile()
    {
        // arrange
        var guard = NewGuard();

        // act
        await TakeOver(guard);

        // assert
        File.ReadAllText(guard.PidFilePath).Should().Be(Environment.ProcessId.ToString());
    }

    [WindowsFact(NamedEventsReason)]
    public async Task TakeOverShouldThrowWhenCalledTwice()
    {
        // arrange
        var guard = NewGuard();
        await TakeOver(guard);

        // act
        var act = () => { guard.TakeOver(); };

        // assert
        act.Should().Throw<InvalidOperationException>();
    }

    [WindowsFact(NamedEventsReason)]
    public async Task SecondGuardShouldTakeTheNameWhenHolderLeavesWhenAsked()
    {
        // arrange
        var holder = NewGuard();
        await TakeOver(holder);
        var challenger = NewGuard();

        // act
        var isTaken = await TakeOver(challenger);

        // assert
        isTaken.Should().BeTrue();
        challenger.Outcome.Should().Be(SingleInstanceOutcome.OtherInstanceExited);
        holder.ExitRequestCount.Should().Be(1);
        challenger.ExitRequestCount.Should().Be(0);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task SecondGuardShouldEndHolderThatDoesNotAnswer()
    {
        // arrange
        var holder = NewGuard(isStuck: true);
        await TakeOver(holder);
        var challenger = NewGuard(handOverTimeout: ShortTimeout);
        challenger.EndHolder = () => {
            holder.ReleaseExit.Set();
            return true;
        };

        // act
        var isTaken = await TakeOver(challenger);

        // assert
        isTaken.Should().BeTrue();
        challenger.Outcome.Should().Be(SingleInstanceOutcome.OtherInstanceEnded);
        holder.ExitRequestCount.Should().Be(1);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task SecondGuardShouldGiveUpWhenHolderCanNotBeEnded()
    {
        // arrange
        var holder = NewGuard(isStuck: true);
        await TakeOver(holder);
        var challenger = NewGuard(handOverTimeout: ShortTimeout);
        challenger.EndHolder = () => false;

        // act
        var isTaken = await TakeOver(challenger);

        // assert
        isTaken.Should().BeFalse();
        challenger.Outcome.Should().Be(SingleInstanceOutcome.OtherInstanceRemained);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task SecondGuardShouldGiveUpWhenEndedHolderStillHoldsTheName()
    {
        // arrange
        var holder = NewGuard(isStuck: true);
        await TakeOver(holder);
        var challenger = NewGuard(handOverTimeout: ShortTimeout);
        challenger.EndHolder = () => true;

        // act
        var isTaken = await TakeOver(challenger);

        // assert
        isTaken.Should().BeFalse();
        challenger.Outcome.Should().Be(SingleInstanceOutcome.OtherInstanceRemained);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task DisposedGuardShouldReleaseTheName()
    {
        // arrange
        var holder = NewGuard();
        await TakeOver(holder);
        holder.Dispose();
        var next = NewGuard();

        // act
        var isTaken = await TakeOver(next);

        // assert
        isTaken.Should().BeTrue();
        next.Outcome.Should().Be(SingleInstanceOutcome.NoOtherInstance);
        holder.ExitRequestCount.Should().Be(0, "disposing is not an exit request");
    }

    [WindowsFact(NamedEventsReason)]
    public async Task RequestSentBeforeTakingTheNameShouldBeIgnored()
    {
        // arrange
        using var staleRequest = new EventWaitHandle(
            false, EventResetMode.AutoReset, TestGuard.GetReplaceEventNameFor(_name));
        staleRequest.Set();
        var guard = NewGuard();

        // act
        await TakeOver(guard);
        await Task.Delay(ShortTimeout);

        // assert
        guard.ExitRequestCount.Should().Be(0);
    }

    [WindowsFact(NamedEventsReason)]
    public async Task TakeOverShouldSucceedAndReportErrorWhenGuardCanNotWork()
    {
        // arrange
        var guard = NewGuard(pidFilePath: Path.Combine(_directory, "missing", "instance.pid"));

        // act
        var isTaken = await TakeOver(guard);

        // assert
        isTaken.Should().BeTrue("a broken guard must not keep the process from starting");
        guard.Outcome.Should().Be(SingleInstanceOutcome.Failed);
        guard.Error.Should().NotBeNull();
    }

    [WindowsTheory(NamedEventsReason)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("2147483646")]
    public void TryEndHolderShouldReturnFalseWhenPidFileDoesNotPointToProcess(string? pidFileContent)
    {
        // arrange
        var pidFilePath = Path.Combine(_directory, "instance.pid");
        if (pidFileContent != null)
            File.WriteAllText(pidFilePath, pidFileContent);
        var guard = NewGuard(pidFilePath: pidFilePath);

        // act
        var result = guard.TryEndHolderByDefault();

        // assert
        result.Should().BeFalse();
    }

    // Private methods

    private TestGuard NewGuard(
        bool isStuck = false,
        TimeSpan? handOverTimeout = null,
        string? pidFilePath = null)
    {
        var guard = new TestGuard(_name, pidFilePath ?? Path.Combine(_directory, "instance.pid")) {
            IsStuck = isStuck,
            HandOverTimeout = handOverTimeout ?? WaitTimeout,
            EndTimeout = ShortTimeout,
        };
        _guards.Add(guard);
        return guard;
    }

    private static Task<bool> TakeOver(TestGuard guard)
        => guard.TakeOver().WaitAsync(WaitTimeout);

    // Nested types

    private sealed class TestGuard : SingleInstanceGuard
    {
        private int _exitRequestCount;

        public bool IsStuck { get; init; }
        public ManualResetEventSlim ReleaseExit { get; } = new(false);
        public Func<bool>? EndHolder { get; set; }
        public int ExitRequestCount => Volatile.Read(ref _exitRequestCount);

        public TestGuard(string name, FilePath pidFilePath)
            : base(name, pidFilePath)
            => ExitRequested += OnExit;

        public static string GetReplaceEventNameFor(string name)
            => GetReplaceEventName(name);

        public bool TryEndHolderByDefault()
            => base.TryEndHolder();

        protected override bool TryEndHolder()
            => EndHolder?.Invoke() ?? base.TryEndHolder();

        // Private methods

        private void OnExit()
        {
            Interlocked.Increment(ref _exitRequestCount);
            if (IsStuck)
                ReleaseExit.Wait();
        }
    }
}
