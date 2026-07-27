using ActualChat.Blobs.Internal;

namespace ActualChat.Core.Server.UnitTests.Blobs;

public class BlobPathTest
{
    private static readonly IServiceProvider Services = new ServiceCollection()
        .AddLogging()
        .BuildServiceProvider();

    [Fact]
    public void GetScopeTest()
    {
        // act
        var withoutScope = BlobPath.GetScope("a");
        var withScope = BlobPath.GetScope("a/b");

        // assert
        withoutScope.Should().Be("");
        withScope.Should().Be("a");
    }

    [Theory]
    [InlineData("media/4F7xR2M9pQ6sT8vW3yK5/123.png")]
    [InlineData("audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0.webm")]
    [InlineData("upload-temp/0123456789ABCDEFGHIJ")]
    [InlineData("upload-temp/0123456789ABCDEFGHIJ.metadata")]
    public async Task AcceptsNestedBlobId(string blobId)
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));

        // act
        var exists = await storage.Exists(blobId, CancellationToken.None);

        // assert
        exists.Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside/file")]
    [InlineData("media/../../outside/file")]
    public async Task RejectsPathOutsideBaseDirectory(string blobId)
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RejectsDecodedDotSegments()
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));
        var blobId = Uri.UnescapeDataString("media/%2e%2e/outside/file");

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(".")]
    [InlineData("media/./file")]
    [InlineData("media/../file")]
    public async Task RejectsDotSegments(string blobId)
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RejectsMixedSeparators()
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));
        var blobId = Uri.UnescapeDataString("media/%2e%2e%5coutside/file");

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RejectsRootedPath()
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));
        var blobId = Path.Combine(Path.GetPathRoot(AppContext.BaseDirectory)!, "outside", "file");

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\C:\outside\file")]
    [InlineData("NUL")]
    [InlineData("media/COM1.png")]
    public async Task RejectsDevicePath(string blobId)
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RejectsControlCharacters()
    {
        // arrange
        await using var storage = CreateStorage(Path.Combine(AppContext.BaseDirectory, "blob-base"));
        var blobId = "media/file\u0001.png";

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RejectsSiblingDirectoryPrefix()
    {
        // arrange
        var parentDirectory = Path.Combine(AppContext.BaseDirectory, "blob-parent");
        await using var storage = CreateStorage(Path.Combine(parentDirectory, "base"));
        var blobId = Path.Combine(parentDirectory, "base-evil", "file");

        // act
        var act = () => storage.Exists(blobId, CancellationToken.None);

        // assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static LocalFolderBlobStorage CreateStorage(string baseDirectory)
        => new(new LocalFolderBlobStorage.Options { BaseDirectory = baseDirectory }, Services);
}
