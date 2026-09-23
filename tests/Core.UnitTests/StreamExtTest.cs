using ActualChat.IO;
using ActualLab.IO;

namespace ActualChat.Core.UnitTests;

public class StreamExtTest : IDisposable
{
    private readonly FilePath _targetPath = (FilePath)Path.GetTempPath() & Path.GetRandomFileName();

    public void Dispose()
        => File.Delete(_targetPath);

    [Fact]
    public async Task ShouldCopyContentWithoutProgress()
    {
        // arrange
        var content = CreateContent(100_000);
        var source = new MemoryStream(content);

        // act
        await source.CopyToFile(_targetPath);

        // assert
        (await File.ReadAllBytesAsync(_targetPath)).Should().Equal(content);
    }

    [Fact]
    public async Task ShouldCopyContentWhileReportingProgress()
    {
        // arrange
        var content = CreateContent(1_000_000);
        var source = new MemoryStream(content);
        var reported = new List<double>();

        // act
        await source.CopyToFile(_targetPath, content.Length, new ForkableProgress(x => reported.Add(x)));

        // assert
        (await File.ReadAllBytesAsync(_targetPath)).Should().Equal(content);
        reported.Should().NotBeEmpty();
        reported.Should().BeInAscendingOrder();
        reported.Should().OnlyHaveUniqueItems(); // One report per whole percent
        reported.Should().AllSatisfy(x => x.Should().BeInRange(0, 100));
        reported[^1].Should().Be(100);
    }

    [Fact]
    public async Task ShouldScaleProgressToTheForkItReportsInto()
    {
        // arrange
        var content = CreateContent(1_000_000);
        var source = new MemoryStream(content);
        var reported = new List<double>();
        var forks = new ForkableProgress(x => reported.Add(x)).Fork(2);

        // act
        await source.CopyToFile(_targetPath, content.Length, forks[1]);

        // assert
        reported[^1].Should().Be(100);
        reported.Should().AllSatisfy(x => x.Should().BeInRange(50, 100));
    }

    [Fact]
    public async Task ShouldCopyWithoutProgressWhenLengthIsUnknown()
    {
        // arrange
        var content = CreateContent(100_000);
        var source = new MemoryStream(content);
        var reported = new List<double>();

        // act
        await source.CopyToFile(_targetPath, null, new ForkableProgress(x => reported.Add(x)));

        // assert
        (await File.ReadAllBytesAsync(_targetPath)).Should().Equal(content);
        reported.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldStopAtFullWhenTheSourceOutgrowsItsLength()
    {
        // arrange - a length the source overruns, e.g. a Content-Length that undercounts
        var content = CreateContent(100_000);
        var source = new MemoryStream(content);
        var reported = new List<double>();

        // act
        await source.CopyToFile(_targetPath, content.Length / 2, new ForkableProgress(x => reported.Add(x)));

        // assert
        (await File.ReadAllBytesAsync(_targetPath)).Should().Equal(content);
        reported.Should().AllSatisfy(x => x.Should().BeInRange(0, 100));
        reported[^1].Should().Be(100);
    }

    [Fact]
    public async Task ShouldStopOnCancellation()
    {
        // arrange
        var source = new MemoryStream(CreateContent(10_000_000));
        using var cts = new CancellationTokenSource();
        var reported = new List<double>();
        var progress = new ForkableProgress(x => {
            reported.Add(x);
            // ReSharper disable once AccessToDisposedClosure
            cts.Cancel();
        });

        // act
        var copy = () => source.CopyToFile(_targetPath, source.Length, progress, cts.Token);

        // assert
        await copy.Should().ThrowAsync<OperationCanceledException>();
        reported.Should().ContainSingle();
    }

    // Private methods

    private static byte[] CreateContent(int length)
    {
        var content = new byte[length];
        Random.Shared.NextBytes(content);
        return content;
    }
}
