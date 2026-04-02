using ActualChat.Uploads;
using Google.Cloud.Storage.V1;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class GoogleCloudVideoUploadProcessorTest
{
    private readonly GoogleCloudVideoUploadProcessor _processor;

    public GoogleCloudVideoUploadProcessorTest()
    {
        var storageClient = new Mock<StorageClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger<GoogleCloudVideoUploadProcessor>>(MockBehavior.Loose);
        _processor = new GoogleCloudVideoUploadProcessor(
            storageClient.Object,
            "test-bucket",
            "test-project",
            "us-central1",
            logger.Object);
    }

    [Theory]
    [InlineData("video/mp4", true)]
    [InlineData("video/webm", true)]
    [InlineData("video/quicktime", true)]
    [InlineData("image/jpeg", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/pdf", false)]
    public void Supports_ReturnsExpectedResult(string contentType, bool expected)
        => _processor.Supports(contentType, default).Should().Be(expected);

    [Fact]
    public async Task Process_WithNonBlobFile_ThrowsInvalidOperationException()
    {
        var streamFile = new UploadedStreamFile("video.mp4", "video/mp4", 100, () => Task.FromResult(Stream.Null));

        var act = () => _processor.Process(streamFile, null, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{nameof(UploadedBlobFile)}*");
    }
}
