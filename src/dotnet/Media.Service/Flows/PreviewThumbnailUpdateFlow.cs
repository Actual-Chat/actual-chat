using ActualChat.Flows;
using MemoryPack;

namespace ActualChat.Media.Flows;

[Flow(DelayQuanta = 5)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PreviewThumbnailUpdateFlow : PeriodicFlow
{
    private ImageGrabber ImageGrabber => field ??= Services.GetRequiredService<ImageGrabber>();

    public static string GetArguments(string url)
        => url.ToBase64();

    protected override async ValueTask<FlowReadiness> Prepare(CancellationToken cancellationToken)
    {
        var url = Id.Arguments.FromBase64();
        return await ImageGrabber.NeedsUpdate(url, cancellationToken).ConfigureAwait(false)
            ? FlowReadiness.Ready
            : "No update needed";
    }

    protected override async ValueTask<Moment> Run(CancellationToken cancellationToken)
    {
        var url = Id.Arguments.FromBase64();
        await ImageGrabber.UpdateExisting(url, cancellationToken).ConfigureAwait(false);
        return Moment.MaxValue;
    }
}
