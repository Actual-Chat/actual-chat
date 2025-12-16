using ActualChat.Flows;
using ActualChat.Media.Module;
using MemoryPack;

namespace ActualChat.Media.Flows;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class PreviewThumbnailUpdateFlow : PeriodicFlow
{
    private MediaSettings Settings => field ??= Services.GetRequiredService<MediaSettings>();
    private ImageGrabber ImageGrabber => field ??= Services.GetRequiredService<ImageGrabber>();

    public static string GetArguments(string url)
        => url.ToBase64();

    protected override ValueTask<Moment> GetNextRunAt(CancellationToken cancellationToken)
        => new(LastRunAt + Settings.LinkPreviewUpdatePeriod);

    protected override async Task Run(CancellationToken cancellationToken)
    {
        var url = Id.Arguments.FromBase64();
        await ImageGrabber.UpdateExisting(url, cancellationToken).ConfigureAwait(false);
    }
}
