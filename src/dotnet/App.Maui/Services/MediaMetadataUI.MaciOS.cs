using ActualChat.UI.Blazor.App.Services;
using CoreGraphics;
using Foundation;
using MediaPlayer;
using UIKit;

namespace ActualChat.App.Maui.Services;

public partial class MediaMetadataUI
{
    private static readonly UIImage DefaultUIImage = new();

    public partial void SetPlayback(MediaMetadata metadata, bool isStreaming)
        => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
            Title = metadata.Title,
            Artist = metadata.Artist,
            Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240), requestHandler: _ => GetImage(metadata.ImageUrl)),
            IsLiveStream = isStreaming,
        };

    public partial void SetRecording(MediaMetadata metadata)
        => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
            Title = metadata.Title,
            Artist = metadata.Artist,
            Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(320, 240), requestHandler: _ => GetImage(metadata.ImageUrl)),
            IsLiveStream = true,
        };

    public partial void Reset()
        => MPNowPlayingInfoCenter.DefaultCenter.NowPlaying = new MPNowPlayingInfo {
            Title = string.Empty,
            Artist = string.Empty,
            Artwork = new MPMediaItemArtwork(boundsSize: new CGSize(0, 0), requestHandler: _ => DefaultUIImage),
            IsLiveStream = false,
        };

    private static UIImage GetImage(string imageUri)
    {
        try
        {
            if (imageUri.StartsWith(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return UIImage.LoadFromData(NSData.FromUrl(new NSUrl(imageUri))) ?? DefaultUIImage;

            return DefaultUIImage;
        }
        catch
        {
            return DefaultUIImage;
        }
    }

}
