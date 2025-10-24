using ActualChat.UI.Blazor.App.Services;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui.Services;

public partial class MediaMetadataUI
{
    [field: AllowNull, MaybeNull]
    private static UIImage DefaultUIImage => field ??= new ();

    public partial Task SetPlayback(MediaMetadata metadata, bool isStreaming)
        => Task.CompletedTask;
    // TODO(FC): use live activities
        // => Invoke(() =>
        //     MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
        //         Title = metadata.Title,
        //         Artist = metadata.Artist,
        //         Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240),
        //             requestHandler: _ => GetImage(metadata.ImageUrl)),
        //         IsLiveStream = isStreaming,
        //     });

    public partial Task SetRecording(MediaMetadata metadata)
        => Task.CompletedTask;
        // => Invoke(() =>
        //     MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
        //         Title = metadata.Title,
        //         Artist = metadata.Artist,
        //         Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240),
        //             requestHandler: _ => GetImage(metadata.ImageUrl)),
        //         IsLiveStream = true,
        //     });

    public partial Task Reset()
        => Task.CompletedTask;
        // => Invoke(() =>
        //     MPNowPlayingInfoCenter.DefaultCenter.NowPlaying =
        //         new MPNowPlayingInfo {
        //             Title = string.Empty,
        //             Artist = string.Empty,
        //             Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(0, 0), requestHandler: _ => DefaultUIImage),
        //             IsLiveStream = false,
        //         });

    private static UIImage GetImage(string imageUri)
    {
        UIImage? result = null;
        UIApplication.SharedApplication.InvokeOnMainThread(() => {
            try {
                if (imageUri.StartsWith(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                    result = UIImage.LoadFromData(NSData.FromUrl(new NSUrl(imageUri))) ?? DefaultUIImage;

                result = DefaultUIImage;
            }
            catch (Exception e) {
                result = DefaultUIImage;
            }
        });
        return result!;
    }

    // private Task Invoke(Action action, [CallerMemberName] string name = "")
    //     => MainThread.InvokeOnMainThreadAsync(action).Catch(Log, "Failed to invoke '{Name}'", name);
}
