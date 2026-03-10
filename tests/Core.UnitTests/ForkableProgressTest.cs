namespace ActualChat.Core.UnitTests;

public class ForkableProgressTest
{
    [Fact]
    public void ReportShouldScaleToFullRange()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        progress.Report(0);
        progress.Report(50);
        progress.Report(100);

        // assert
        reported.Should().Equal([0, 50, 100]);
    }

    [Fact]
    public void ForkEqualPartsShouldDivideRangeEqually()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var forks = progress.Fork(2);
        forks[0].Report(0);
        forks[0].Report(100);
        forks[1].Report(0);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([0, 50, 50, 100]);
    }

    [Fact]
    public void ForkFourWayShouldDivideCorrectly()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var forks = progress.Fork(4);
        forks[0].Report(100);
        forks[1].Report(100);
        forks[2].Report(100);
        forks[3].Report(100);

        // assert
        reported.Should().Equal([25, 50, 75, 100]);
    }

    [Fact]
    public void ForkWeightedPartsShouldDistributeByWeight()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var (first, second) = progress.Fork(0.2, 0.8);
        first.Report(0);
        first.Report(100);
        second.Report(0);
        second.Report(50);
        second.Report(100);

        // assert
        reported.Should().Equal([0, 20, 20, 60, 100]);
    }

    [Fact]
    public void ForkWeightedTupleShouldReturnCorrectForks()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var (first, second) = progress.Fork(0.3, 0.7);
        first.Report(100);
        second.Report(100);

        // assert
        reported.Should().Equal([30, 100]);
    }

    [Fact]
    public void ForkThreeWeightedTupleShouldReturnCorrectForks()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var (first, second, third) = progress.Fork(0.1, 0.2, 0.7);
        first.Report(100);
        second.Report(100);
        third.Report(100);

        // assert
        reported.Should().Equal([10, 30, 100]);
    }

    [Fact]
    public void ForkNestedShouldComposeCorrectly()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var forks = progress.Fork(2);
        var nestedForks = forks[0].Fork(2);
        nestedForks[0].Report(100);
        nestedForks[1].Report(100);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([25, 50, 100]);
    }

    [Fact]
    public void ForkNestedWithWeightsShouldComposeCorrectly()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var (transcoding, upload) = progress.Fork(0.2, 0.8);
        var fileForks = upload.Fork(2);
        transcoding.Report(100);
        fileForks[0].Report(100);
        fileForks[1].Report(100);

        // assert
        reported.Should().Equal([20, 60, 100]);
    }

    [Fact]
    public void ForkZeroCountShouldThrow()
    {
        // arrange
        var progress = new ForkableProgress(_ => { });

        // act
        var act = () => progress.Fork(0);

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ForkNegativeCountShouldThrow()
    {
        // arrange
        var progress = new ForkableProgress(_ => { });

        // act
        var act = () => progress.Fork(-1);

        // assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ForkEmptyWeightsShouldThrow()
    {
        // arrange
        var progress = new ForkableProgress(_ => { });

        // act
        var act = () => progress.Fork();

        // assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForkZeroTotalWeightShouldThrow()
    {
        // arrange
        var progress = new ForkableProgress(_ => { });

        // act
        var act = () => progress.Fork([0, 0, 0]);

        // assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForkNonNormalizedWeightsShouldNormalize()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var forks = progress.Fork([1, 3]);
        forks[0].Report(100);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([25, 100]);
    }

    [Fact]
    public void ReportPartialProgressShouldInterpolateCorrectly()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));
        var forks = progress.Fork(4);

        // act
        forks[0].Report(50);

        // assert
        reported.Should().Equal([12.5]);
    }

    [Fact]
    public void IProgressInterfaceShouldWork()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new ForkableProgress(v => reported.Add(v));

        // act
        progress.Report(42);

        // assert
        reported.Should().Equal([42]);
    }

    [Fact]
    public void SimulateRealWorldScenarioMultiChatMultiFile()
    {
        // arrange
        var reported = new List<double>();
        var progress = new ForkableProgress(v => reported.Add(v));

        // act
        var chatForks = progress.Fork(3);
        var chat1Files = chatForks[0].Fork(2);
        var (transcode1, upload1) = chat1Files[0].Fork(0.2, 0.8);
        transcode1.Report(100);
        upload1.Report(100);
        chat1Files[1].Report(100);
        chatForks[1].Report(100);
        chatForks[2].Report(100);

        // assert
        reported.Last().Should().BeApproximately(100, 0.001);
    }

    // ProgressExt tests (using SyncProgress to avoid async callback issues)

    [Fact]
    public void ExtensionForkEqualPartsShouldWork()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new SyncProgress<double>(v => reported.Add(v));

        // act
        var forks = progress.Fork(2);
        forks[0].Report(100);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([50, 100]);
    }

    [Fact]
    public void ExtensionForkWeightedArrayShouldWork()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new SyncProgress<double>(v => reported.Add(v));

        // act
        var forks = progress.Fork([1, 3]);
        forks[0].Report(100);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([25, 100]);
    }

    [Fact]
    public void ExtensionForkTwoWeightsShouldWork()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new SyncProgress<double>(v => reported.Add(v));

        // act
        var (first, second) = progress.Fork(0.2, 0.8);
        first.Report(100);
        second.Report(100);

        // assert
        reported.Should().Equal([20, 100]);
    }

    [Fact]
    public void ExtensionForkThreeWeightsShouldWork()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new SyncProgress<double>(v => reported.Add(v));

        // act
        var (first, second, third) = progress.Fork(0.1, 0.2, 0.7);
        first.Report(100);
        second.Report(100);
        third.Report(100);

        // assert
        reported.Should().Equal([10, 30, 100]);
    }

    [Fact]
    public void ExtensionForkShouldAllowNestedForking()
    {
        // arrange
        var reported = new List<double>();
        IProgress<double> progress = new SyncProgress<double>(v => reported.Add(v));

        // act
        var forks = progress.Fork(2);
        var nested = forks[0].Fork(2);
        nested[0].Report(100);
        nested[1].Report(100);
        forks[1].Report(100);

        // assert
        reported.Should().Equal([25, 50, 100]);
    }

    /// <summary>
    /// Synchronous IProgress implementation for testing (unlike Progress{T} which uses SynchronizationContext).
    /// </summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
