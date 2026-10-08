using ActualChat.Uploads;
using FFMpegCore;
using Google.Cloud.Storage.V1;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public sealed class GoogleCloudVideoUploadProcessorTest
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
    public void SupportsShouldAcceptOnlyVideo(string contentType, bool expected)
    {
        // act
        var result = _processor.Supports(contentType, default);

        // assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("h264", true)]
    [InlineData("hevc", true)]
    [InlineData("vp9", true)]
    [InlineData("mpeg4", true)]
    [InlineData("prores", true)]
    [InlineData("av1", false)]
    [InlineData("gif", false)]
    public void CanTranscodeShouldAcceptOnlyTranscoderInputCodecs(string codecName, bool expected)
    {
        // arrange
        var videoStream = new VideoStream { CodecName = codecName };

        // act
        var result = GoogleCloudVideoUploadProcessor.CanTranscode(videoStream);

        // assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task ProcessShouldRejectFileNotInBlobStorage()
    {
        // arrange
        var streamFile = new UploadedStreamFile("video.mp4", "video/mp4", 100, () => Task.FromResult(Stream.Null));

        // act
        var act = () => _processor.Process(streamFile, null, CancellationToken.None);

        // assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage($"*{nameof(UploadedBlobFile)}*");
    }
}
