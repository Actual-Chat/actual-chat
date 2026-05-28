using AVFoundation;
using CoreMedia;

namespace ActualChat.Maui;

public static class AVAssetTrackExt
{
    extension(AVAssetTrack track)
    {
        public CMVideoCodecType VideoCodecType
            => track.FormatDescriptions.Select(x => x.VideoCodecType).FirstOrDefault();

        public bool IsRotated {
            get {
                var t = track.PreferredTransform;
                return Math.Abs(t.A) < 0.01 && Math.Abs(t.D) < 0.01;
            }
        }

        /// <summary>
        /// Returns the actual video resolution, accounting for rotation (e.g., portrait recordings).
        /// </summary>
        public CGSize ActualResolution {
            get {
                var size = track.NaturalSize;
                return track.IsRotated
                    ? new CGSize(Math.Abs(size.Height), Math.Abs(size.Width))
                    : new CGSize(Math.Abs(size.Width), Math.Abs(size.Height));
            }
        }
    }
}
