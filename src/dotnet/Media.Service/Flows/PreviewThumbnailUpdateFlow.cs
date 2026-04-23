using ActualChat.Flows;
using ActualChat.Media.Module;

namespace ActualChat.Media.Flows;

[Flow(DelayQuanta = 1)] // Extra resumes are fine
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PreviewThumbnailUpdateFlow : ThrottledUpdateFlow
{
    private MediaSettings MediaSettings => field ??= Services.GetRequiredService<MediaSettings>();
    private ImageGrabber ImageGrabber => field ??= Services.GetRequiredService<ImageGrabber>();

    protected override TimeSpan ThrottlePeriod => MediaSettings.LinkPreviewUpdatePeriod;

    protected override async ValueTask Run(CancellationToken cancellationToken)
        => await ImageGrabber.UpdateExisting(Target, cancellationToken).ConfigureAwait(false);
}
