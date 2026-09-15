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
        var resolvedTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx50, isMobile: false);
        var otherTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12, isMobile: false);

        // assert
        resolvedTotal.Should().Be(new ImageQualityMenuModel.PresetTotal(18_000_000, true));
        otherTotal!.Value.IsExact.Should().BeFalse();
        ImageQualityMenuModel.IsDeclinedAt(images, ImageQualityPreset.Mpx50, isMobile: false).Should().BeTrue();
        ImageQualityMenuModel.IsDeclinedAt(images, ImageQualityPreset.Mpx12, isMobile: false).Should().BeFalse();
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
        var total = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx3, isMobile: false);
        var resolvedTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12, isMobile: false);

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
        var total = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.OriginalWithExif, isMobile: false);

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
        var resizeTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12, isMobile: false);
        var originalTotal = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Original, isMobile: false);

        // assert
        resizeTotal.Should().BeNull();
        originalTotal.Should().Be(new ImageQualityMenuModel.PresetTotal(3_000_000, true));
    }

    [Fact]
    public void MobileShouldPriceADeclinedResizeAtTheSourceLength()
    {
        // arrange - a budget past the mobile cap, which no shipped preset has since the cap was
        // raised to 50 MP; the guard is what a future larger budget would meet
        var size = new Size2D(12288, 9216);
        var pastTheCap = new ImageQualityBudget(120_000_000, 16384);
        var attachment = NewImage("image/jpeg", 18_000_000, size);
        var images = new List<Attachment> { attachment };

        // act
        var onMobile = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx50, isMobile: true);

        // assert
        ImageQualityMenuModel.WillDecline(size, pastTheCap, isMobile: true).Should().BeTrue();
        ImageQualityMenuModel.WillDecline(size, pastTheCap, isMobile: false).Should().BeFalse(
            "the guard is about what a phone can afford, not what the image is");
        onMobile!.Value.IsExact.Should().BeFalse("every shipped budget now fits under the cap");
    }

    [Fact]
    public void NoShippedPresetShouldDeclineOnAPhone()
    {
        // arrange - the largest source the server accepts, so each preset's target sits at its budget
        var size = new Size2D(Constants.Attachments.MaxImageSize, 7812);

        // assert
        foreach (var preset in Enum.GetValues<ImageQualityPreset>())
            ImageQualityMenuModel.WillDecline(size, preset.GetBudget(), isMobile: true)
                .Should().BeFalse($"{preset}'s budget must stay under the mobile encode cap");
    }

    [Fact]
    public void MobileShouldStillEstimateAPresetThatResizesUnderTheCap()
    {
        // arrange - the same 50 MP source at Mpx12, whose target is 12.6 MP. SelectedQuality is
        // deliberately another preset, so the row queried below is not the already-resolved one
        var attachment = NewImage("image/jpeg", 18_000_000, new Size2D(8160, 6144)) with {
            SelectedQuality = ImageQualityPreset.Mpx3,
        };
        var images = new List<Attachment> { attachment };

        // act
        var total = ImageQualityMenuModel.GetTotal(images, 0, ImageQualityPreset.Mpx12, isMobile: true);

        // assert
        total!.Value.IsExact.Should().BeFalse("a target under the cap is encoded, so the row is an estimate");
        ImageQualityMenuModel.IsDeclinedAt(images, ImageQualityPreset.Mpx12, isMobile: true).Should().BeFalse();
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
