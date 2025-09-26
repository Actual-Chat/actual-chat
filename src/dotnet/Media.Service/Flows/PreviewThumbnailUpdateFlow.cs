using ActualChat.Flows;
using ActualChat.Media.Module;
using MemoryPack;

namespace ActualChat.Media.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PreviewThumbnailUpdateFlow : Flow, IHasLastRunAt
{
    [field: AllowNull, MaybeNull]
    private MediaSettings Settings => field ??= Host.Services.GetRequiredService<MediaSettings>();
    [field: AllowNull, MaybeNull]
    private IMediaBackend MediaBackend => field ??= Host.Services.GetRequiredService<IMediaBackend>();
    [field: AllowNull, MaybeNull]
    private ImageGrabber ImageGrabber => field ??= Host.Services.GetRequiredService<ImageGrabber>();

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Moment LastRunAt { get; private set; }

    public static string GetArguments(string url)
        => url.ToBase64();

    protected override async Task<FlowTransition> OnReset(CancellationToken cancellationToken)
    {
        await Run(cancellationToken).ConfigureAwait(false);
        return WaitForEvent(nameof(OnReset), Settings.LinkPreviewUpdatePeriod);
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        LastRunAt = Host.Clocks.SystemClock.Now;
        var url = Id.Arguments.FromBase64();
        await ImageGrabber.UpdateExisting(url, cancellationToken).ConfigureAwait(false);
    }
}
