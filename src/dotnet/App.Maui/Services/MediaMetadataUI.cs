using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
#if IOS || MACCATALYST
using Foundation;
using UIKit;
#endif

namespace ActualChat.App.Maui.Services;

/// <summary>
/// Platform-specific implementation of media metadata display for lock screen and notifications.
/// </summary>
public class MediaMetadataUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IMediaMetadataUI
{
    public Task SetPlayback(MediaMetadata metadata, bool isStreaming)
        => Task.CompletedTask;

    public Task SetRecording(MediaMetadata metadata)
        => Task.CompletedTask;

    public Task Reset()
        => Task.CompletedTask;

#if false // was: #if IOS || MACCATALYST
    private static UIImage DefaultUIImage => field ??= new ();

    public Task SetPlayback(MediaMetadata metadata, bool isStreaming)
        // TODO(FC): use live activities
        => Invoke(() =>
            MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
                Title = metadata.Title,
                Artist = metadata.Artist,
                Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240),
                    requestHandler: _ => GetImage(metadata.ImageUrl)),
                IsLiveStream = isStreaming,
            });

    public Task SetRecording(MediaMetadata metadata)
        => Invoke(() =>
            MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
                Title = metadata.Title,
                Artist = metadata.Artist,
                Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240),
                    requestHandler: _ => GetImage(metadata.ImageUrl)),
                IsLiveStream = true,
            });

    public Task Reset()
        => Invoke(() =>
            MPNowPlayingInfoCenter.DefaultCenter.NowPlaying =
                new MPNowPlayingInfo {
                    Title = string.Empty,
                    Artist = string.Empty,
                    Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(0, 0), requestHandler: _ => DefaultUIImage),
                    IsLiveStream = false,
                });

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

    private Task Invoke(Action action, [CallerMemberName] string name = "")
        => MainThread.InvokeOnMainThreadAsync(action).Catch(Log, "Failed to invoke '{Name}'", name);
#endif
}
