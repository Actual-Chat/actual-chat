using AVFoundation;
using CoreGraphics;
using CoreMedia;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace ActualChat.App.Maui.Video;

// Status/Flush/Enqueue on AVSampleBufferDisplayLayer are deprecated for the
// AVSampleBufferVideoRenderer API introduced in macCatalyst 18, but the app deploys to
// macCatalyst 14+, where the layer API is the only one available. Keep using it.
#pragma warning disable CA1422

/// <summary>
/// A <see cref="UIView"/> backed by an <see cref="AVSampleBufferDisplayLayer"/>: it takes
/// compressed H.264 <see cref="CMSampleBuffer"/>s (built by <see cref="H264SampleBufferBuilder"/>)
/// and hardware-decodes + composites them on the GPU, so decoded pixels never cross into
/// managed code. One instance renders one remote tile; <see cref="NativeVideoOverlayHost"/>
/// positions it over the WKWebView.
/// </summary>
internal sealed class SampleBufferDisplayView : UIView
{
    // Raised (on the enqueue thread) when the layer's decoder has failed and been flushed;
    // the consumer must feed a fresh keyframe to resume. Flush already happened.
    public event Action? DecodingFailed;

    [Export("layerClass")]
    public static Class LayerClass()
        => new(typeof(AVSampleBufferDisplayLayer));

    private AVSampleBufferDisplayLayer SampleLayer => (AVSampleBufferDisplayLayer)Layer;

    public SampleBufferDisplayView()
    {
        // Default video gravity is ResizeAspect (aspect-fit): the tile's CSS aspect-ratio
        // matches the source, so it fills the frame without letterboxing or cropping.
        BackgroundColor = UIColor.Black;
        UserInteractionEnabled = false;
        SampleLayer.MasksToBounds = true;
    }

    public void Enqueue(CMSampleBuffer sampleBuffer)
    {
        var layer = SampleLayer;
        // A failed decoder stays failed until flushed; recover by flushing and asking the
        // caller for a keyframe rather than enqueuing into a dead layer.
        if (layer.Status == AVQueuedSampleBufferRenderingStatus.Failed) {
            layer.Flush();
            DecodingFailed?.Invoke();
            return;
        }

        var attachments = sampleBuffer.GetSampleAttachments(true);
        if (attachments is { Length: > 0 } && attachments[0] is { } settings)
            settings.DisplayImmediately = true;
        layer.Enqueue(sampleBuffer);
    }

    public void Flush()
        => SampleLayer.Flush();

    public void SetCornerRadius(nfloat radius)
        => SampleLayer.CornerRadius = radius;

    public void SetMirrored(bool mirrored)
        => Transform = mirrored ? CGAffineTransform.MakeScale(-1, 1) : CGAffineTransform.MakeIdentity();
}
