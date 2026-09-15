namespace ActualChat.ContentCaching.UnitTests;

public sealed partial class FileSystemContentHandlerTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedFilesShouldUseHashBucketsAndReplaceStalePartials(bool hasPartial)
    {
        // arrange
        var path = _directory & "34" & "NNxDbnMuphfhO2qYixIG5hlnYWr9ZwRUEZpPJTspwlM";
        var partialPath = path + ".p";
        if (hasPartial) {
            Directory.CreateDirectory(path.DirectoryPath);
            await File.WriteAllBytesAsync(partialPath, [1, 2, 3]);
        }
        var downloadCount = 0;
        var handler = Create(new TestSource(_ => {
            downloadCount++;
            return Response("body");
        }));

        // act
        using var response = await handler.Handle(Request());

        // assert
        (await response!.Content.ReadAsStringAsync()).Should().Be("body");
        downloadCount.Should().Be(1);
        File.Exists(path).Should().BeTrue();
        File.Exists(partialPath).Should().BeFalse();
        GetCacheFiles().Should().Equal(path.Value);
    }
}
