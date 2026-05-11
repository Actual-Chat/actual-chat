// Bundle-level keyframe-status helpers.
//
// Per-layer KF status can diverge: encoders may emit a keyframe unilaterally
// on reset/recovery even when only a delta was requested. A bundle is a real
// keyframe only when ALL spatial layers agree.
//
// `applyKeyframePolicy` produces request-side bundles where forceKeyframe
// either holds on every layer or none; the encoder OUTPUT may break that
// uniformity. Anything downstream that asks "is this bundle a keyframe?"
// must use one of these helpers — never read `layers[0]` in isolation.

import type { VideoFrameBundleDto } from 'api';
import type { CapturedBundle, EncodedBundle } from './frame-envelopes';
import type { VideoStreamFrameBundle } from './operators/wire-send';

// Captured-side check: every layer must have forceKeyframe=true.
// applyKeyframePolicy enforces all-or-none, but reading every layer here
// keeps callers honest if that invariant ever weakens.
export function isCapturedBundleKeyFrame(bundle: CapturedBundle): boolean {
    if (bundle.layers.length === 0) return false;
    for (const layer of bundle.layers) {
        if (!layer.forceKeyframe) return false;
    }
    return true;
}

// Encoded-side check: every layer's chunk must be a keyframe.
export function isEncodedBundleKeyFrame(bundle: EncodedBundle): boolean {
    if (bundle.layers.length === 0) return false;
    for (const layer of bundle.layers) {
        if (layer.chunk.type !== 'key') return false;
    }
    return true;
}

// Sender-side wire view: a layer is KF iff keyFrameIndex === index.
export function isVideoStreamBundleKeyFrame(bundle: VideoStreamFrameBundle): boolean {
    if (bundle.layers.length === 0) return false;
    for (const layer of bundle.layers) {
        if (layer.keyFrameIndex !== layer.index) return false;
    }
    return true;
}

// PascalCase wire DTO going onto / off the RpcStream.
export function isVideoFrameBundleDtoKeyFrame(bundle: VideoFrameBundleDto): boolean {
    if (bundle.Layers.length === 0) return false;
    for (const layer of bundle.Layers) {
        if (layer.KeyFrameIndex !== layer.Index) return false;
    }
    return true;
}
