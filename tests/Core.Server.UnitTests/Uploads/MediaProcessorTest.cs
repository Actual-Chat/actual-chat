using ActualChat.Uploads;
using SixLabors.ImageSharp;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class MediaProcessorTest
{
    [Fact]
    public async Task ProcessUpload_MatchingProcessor_DelegatesToIt()
    {
        var input = CreateUploadedFile("video.mp4", "video/mp4");
        var expectedResult = new ProcessedFile(input, new Size(100, 100));

        var processor = new Mock<IUploadProcessor>();
        processor.Setup(p => p.Supports("video/mp4")).Returns(true);
        processor.Setup(p => p.Process(input, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var mediaProcessor = CreateMediaProcessor(processor.Object);
        var result = await mediaProcessor.ProcessUpload(input, null, CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        processor.Verify(p => p.Process(input, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ProcessUpload_NoMatchingProcessor_ReturnsPassthrough()
    {
        var input = CreateUploadedFile("doc.pdf", "application/pdf");

        var processor = new Mock<IUploadProcessor>();
        processor.Setup(p => p.Supports(It.IsAny<string>())).Returns(false);

        var mediaProcessor = CreateMediaProcessor(processor.Object);
        var result = await mediaProcessor.ProcessUpload(input, null, CancellationToken.None);

        result.File.Should().BeSameAs(input);
        result.Size.Should().BeNull();
        result.Thumbnail.Should().BeNull();
        processor.Verify(p => p.Process(It.IsAny<UploadedFile>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpload_FirstMatchWins()
    {
        var input = CreateUploadedFile("video.mp4", "video/mp4");
        var expectedResult = new ProcessedFile(input, new Size(200, 200));

        var first = new Mock<IUploadProcessor>();
        first.Setup(p => p.Supports("video/mp4")).Returns(true);
        first.Setup(p => p.Process(input, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var second = new Mock<IUploadProcessor>();
        second.Setup(p => p.Supports("video/mp4")).Returns(true);

        var mediaProcessor = CreateMediaProcessor(first.Object, second.Object);
        var result = await mediaProcessor.ProcessUpload(input, null, CancellationToken.None);

        result.Should().BeSameAs(expectedResult);
        second.Verify(p => p.Process(It.IsAny<UploadedFile>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUpload_ReportsProgress100_AfterCompletion()
    {
        var input = CreateUploadedFile("video.mp4", "video/mp4");

        var processor = new Mock<IUploadProcessor>();
        processor.Setup(p => p.Supports("video/mp4")).Returns(true);
        processor.Setup(p => p.Process(input, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedFile(input, new Size(100, 100)));

        var progress = new Mock<IProgress<double>>();
        var mediaProcessor = CreateMediaProcessor(processor.Object);
        await mediaProcessor.ProcessUpload(input, progress.Object, CancellationToken.None);

        progress.Verify(p => p.Report(100), Times.Once);
    }

    [Fact]
    public async Task ProcessUpload_NoMatch_DoesNotReportProgress()
    {
        var input = CreateUploadedFile("doc.pdf", "application/pdf");
        var progress = new Mock<IProgress<double>>();

        var mediaProcessor = CreateMediaProcessor();
        await mediaProcessor.ProcessUpload(input, progress.Object, CancellationToken.None);

        progress.Verify(p => p.Report(It.IsAny<double>()), Times.Never);
    }

    private static MediaProcessor CreateMediaProcessor(params IUploadProcessor[] processors)
    {
        var services = new ServiceCollection();
        foreach (var p in processors)
            services.AddSingleton(p);
        // Register IEnumerable<IUploadProcessor> from all singletons
        services.AddSingleton<IEnumerable<IUploadProcessor>>(sp =>
            processors);
        return new MediaProcessor(services.BuildServiceProvider());
    }

    private static UploadedStreamFile CreateUploadedFile(string fileName, string contentType)
        => new(fileName, contentType, 0, () => Task.FromResult<Stream>(Stream.Null));
}
