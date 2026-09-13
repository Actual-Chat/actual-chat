using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImageQualityMenuModelTest
{
    [Fact]
    public void ShouldFlagAnOversizeSourceAsSentAsFile()
    {
        // arrange
        var oversize = NewImage("image/jpeg", 20_000_000, new Size2D(20000, 8000));

        // act
        var isSentAsFile = ImageQualityMenuModel.IsSentAsFile([oversize]);

        // assert
        isSentAsFile.Should().BeTrue();
    }

    [Fact]
    public void ShouldNotFlagAnOrdinarySourceAsSentAsFile()
    {
        // arrange
        var ordinary = NewImage("image/jpeg", 3_000_000, new Size2D(4032, 3024));

        // act
        var isSentAsFile = ImageQualityMenuModel.IsSentAsFile([ordinary]);

        // assert
        isSentAsFile.Should().BeFalse();
    }

    [Fact]
    public void ResolvedPresetRowShouldUseTheRealLengthOnlyOnItsOwnPreset()
    {
        // arrange
        var attachment = NewImage("image/jpeg", 18_000_000, new Size2D(9000, 7000)) with {
            SelectedQuality = ImageQualityPreset.Mpx50,
            IsDeclined = true,
        };
        var images = new List<Attachment> { attachment };

        // act
        var resolvedTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx50);
        var otherTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12);

        // assert
        resolvedTotal.Should().Be(new ImageQualityMenuModel.PresetTotal(18_000_000, true));
        otherTotal!.Value.IsExact.Should().BeFalse();
        ImageQualityMenuModel.IsDeclinedAt(images, ImageQualityPreset.Mpx50).Should().BeTrue();
        ImageQualityMenuModel.IsDeclinedAt(images, ImageQualityPreset.Mpx12).Should().BeFalse();
    }

    [Fact]
    public void ShouldEstimateFromTheSourceFormatEvenAfterItWasReEncodedToJpeg()
    {
        // arrange - already processed once at Mpx12: the top-level type/size/length are the JPEG
        // output's, but Source still carries the original HEIC
        var sourceSize = new Size2D(4032, 3024);
        var source = new AttachmentSource(new TestFileProvider(), "photo.heic", "image/heic", 3_500_000, sourceSize);
        var attachment = new Attachment("photo.jpg", "image/jpeg", 900_000, new Size2D(2016, 1512)) {
            Source = source,
            SelectedQuality = ImageQualityPreset.Mpx12,
        };
        var images = new List<Attachment> { attachment };

        // act
        var total = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx3);
        var resolvedTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12);

        // assert
        var expected = ImageSizeEstimator.Estimate(
            3_500_000, sourceSize, "image/heic", ImageQualityPreset.Mpx3.GetBudget());
        total.Should().Be(new ImageQualityMenuModel.PresetTotal(expected, false));
        // Pins the real upload length (900_000), not the source's (3_500_000), on the resolved row
        resolvedTotal.Should().Be(new ImageQualityMenuModel.PresetTotal(900_000, true));
    }

    [Fact]
    public void OriginalRowShouldUseTheSourceLengthNotTheCurrentOutputLength()
    {
        // arrange - already processed once at Mpx12: Length is the small JPEG output's
        var source = new AttachmentSource(
            new TestFileProvider(), "photo.heic", "image/heic", 8_000_000, new Size2D(4032, 3024));
        var attachment = new Attachment("photo.jpg", "image/jpeg", 900_000, new Size2D(2016, 1512)) {
            Source = source,
            SelectedQuality = ImageQualityPreset.Mpx12,
        };
        var images = new List<Attachment> { attachment };

        // act
        var total = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.OriginalWithExif);

        // assert
        total.Should().Be(new ImageQualityMenuModel.PresetTotal(8_000_000, true));
    }

    [Fact]
    public void UnknownSourceDimensionsShouldMakeAResizeRowUnknownButNotAnOriginalRow()
    {
        // arrange - an unconverted HEIC on Chromium: dimensions unknown, bytes known; SelectedQuality
        // is deliberately a different preset than the one queried below, so the null result is not
        // an artefact of IsProcessing or of Mpx12 happening to be the enum default
        var attachment = NewImage("image/heic", 3_000_000, default) with {
            SelectedQuality = ImageQualityPreset.Mpx3,
            IsProcessing = false,
        };
        var images = new List<Attachment> { attachment };

        // act
        var resizeTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12);
        var originalTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Original);

        // assert
        resizeTotal.Should().BeNull();
        originalTotal.Should().Be(new ImageQualityMenuModel.PresetTotal(3_000_000, true));
    }

    // Private methods

    private static Attachment NewImage(string fileType, long length, Size2D size)
        => new(fileType, fileType, length, size) {
            Source = new AttachmentSource(new TestFileProvider(), fileType, fileType, length, size),
        };

    // Nested types

    private sealed class TestFileProvider : IFileProvider
    {
        public FileMetadata Metadata { get; } = new();
        public Task PrepareForSaving() => Task.CompletedTask;
        public void Initialize(IServiceProvider services) { }
        public Task<bool> CheckAccess() => ActualLab.Async.TaskExt.TrueTask;
        public Task<bool> WhenUserConsentGranted() => ActualLab.Async.TaskExt.TrueTask;
        public Task ClearForRemoving() => Task.CompletedTask;
        public Task<FilePreview> GetPreview(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WhenFileStreamReady() => Task.CompletedTask;
        public UploadSource GetUploadSource() => throw new NotSupportedException();
    }
}
