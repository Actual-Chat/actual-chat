using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class UploadedBlobFileTest
{
    [Fact]
    public async Task Open_DelegatesToGetStream()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var file = new UploadedBlobFile("test.mp4", "video/mp4", 3, "uploads/test.mp4", () => Task.FromResult<Stream>(stream));

        var result = await file.Open();
        result.Should().BeSameAs(stream);
    }

    [Fact]
    public void Length_ReturnsConstructorValue()
    {
        var file = new UploadedBlobFile("test.mp4", "video/mp4", 42, "uploads/test.mp4", () => Task.FromResult<Stream>(Stream.Null));
        file.Length.Should().Be(42);
    }

    [Fact]
    public void With_ContentType_PreservesOtherProperties()
    {
        Func<Task<Stream>> getStream = () => Task.FromResult<Stream>(Stream.Null);
        var file = new UploadedBlobFile("test.mp4", "video/mp4", 100, "uploads/test.mp4", getStream);

        var modified = file with { ContentType = "application/octet-stream" };

        modified.ContentType.Should().Be("application/octet-stream");
        modified.FileName.Value.Should().Be("test.mp4");
        modified.Length.Should().Be(100);
        modified.BlobPath.Should().Be("uploads/test.mp4");
        modified.GetStream.Should().BeSameAs(getStream);
    }
}
