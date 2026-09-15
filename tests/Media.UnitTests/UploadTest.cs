namespace ActualChat.Media.UnitTests;

public sealed class UploadTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void KeepMetadataShouldSurviveTheUploadMetadataFileRoundTrip()
    {
        // arrange
        var metadata = MetadataBag.Empty.Set(nameof(Upload.KeepMetadata), true);
        var upload = new Upload(UploadId.New(), default!, 10, "", metadata);

        // act
        var result = JsonSerializer.Deserialize<Upload>(JsonSerializer.Serialize(upload))!;

        // assert
        upload.KeepMetadata.Should().BeTrue();
        result.KeepMetadata.Should().BeTrue("UploadsBackend stores uploads as System.Text.Json metadata files");
        new Upload(UploadId.New(), default!, 10, "", MetadataBag.Empty).KeepMetadata.Should().BeFalse();
    }
}
