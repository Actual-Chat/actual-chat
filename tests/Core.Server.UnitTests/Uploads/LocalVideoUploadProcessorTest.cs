using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class LocalVideoUploadProcessorTest
{
    private readonly LocalVideoUploadProcessor _processor;

    public LocalVideoUploadProcessorTest()
    {
        var logger = new Mock<ILogger<LocalVideoUploadProcessor>>(MockBehavior.Loose);
        _processor = new LocalVideoUploadProcessor(logger.Object);
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
}
