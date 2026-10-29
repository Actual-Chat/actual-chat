using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class FileUploadQueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const long SmallLength = 1024;
    private const long LargeLength = FileUploadQueue.LargeFileLength;

    [Fact]
    public void SmallFileStartsBesideLargeOnes()
    {
        // arrange
        var queue = new FileUploadQueue();
        var large1 = new TestUpload(LargeLength);
        var large2 = new TestUpload(LargeLength);
        var small = new TestUpload(SmallLength);

        // act
        queue.Enqueue(large1.Operation);
        queue.Enqueue(large2.Operation);
        queue.Enqueue(small.Operation);

        // assert
        large1.Operation.HasStarted.Should().BeTrue();
        large2.Operation.HasStarted.Should().BeFalse();
        small.Operation.HasStarted.Should().BeTrue();
    }

    [Fact]
    public async Task NextLargeFileStartsOnceTheActiveOneCompletes()
    {
        // arrange
        var queue = new FileUploadQueue();
        var large1 = new TestUpload(LargeLength);
        var large2 = new TestUpload(LargeLength);
        queue.Enqueue(large1.Operation);
        queue.Enqueue(large2.Operation);
        large2.Operation.HasStarted.Should().BeFalse();

        // act
        large1.Complete();
        await large1.Operation.Task;

        // assert
        await TestWait.WhenPolled(() =>large2.Operation.HasStarted.Should().BeTrue(), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SmallFilesStillRunTwoAtATime()
    {
        // arrange
        var queue = new FileUploadQueue();
        var uploads = Enumerable.Range(0, 3).Select(_ => new TestUpload(SmallLength)).ToList();

        // act
        foreach (var upload in uploads)
            queue.Enqueue(upload.Operation);

        // assert
        uploads.Select(u => u.Operation.HasStarted).Should().Equal(true, true, false);
    }

    private sealed class TestUpload
    {
        private readonly TaskCompletionSource _whenCompleted = TaskCompletionSourceExt.New();

        public FileUploadOperation Operation { get; }

        public TestUpload(long length)
            => Operation = new FileUploadOperation(length, () => _whenCompleted.Task);

        public void Complete()
            => _whenCompleted.TrySetResult();
    }
}
