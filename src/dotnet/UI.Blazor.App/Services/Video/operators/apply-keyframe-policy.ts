import { from, type PipeOperator } from 'ix-ext';
import type { CapturedBundle, CapturedFrame } from '../frame-envelopes';

export interface KeyframePolicyOptions {
    /** Force a keyframe every Nth bundle. Counter resets on every
     *  triggered keyframe (regardless of trigger reason). */
    keyframeIntervalFrames: number;
    /** Wallclock floor (ms). Wallclock trigger requires a prior
     *  keyframe — first frame qualifies by frame-count or upstream flag. */
    maxKeyframeIntervalMs?: number;
    now?: () => number;
}

/**
 * Sets `forceKeyframe = true` across every layer in a bundle on any
 * of: frame-count interval, wallclock floor, or upstream already
 * raised the flag (any layer with the flag triggers it across the
 * whole bundle).
 *
 * Shallow-clones the layer envelopes when raising the flag so upstream
 * operators see no surprising mutations.
 */
export function applyKeyframePolicy(opts: KeyframePolicyOptions): PipeOperator<CapturedBundle, CapturedBundle> {
    const { keyframeIntervalFrames, maxKeyframeIntervalMs } = opts;
    if (keyframeIntervalFrames <= 0)
        throw new Error('applyKeyframePolicy: keyframeIntervalFrames must be > 0');
    const now = opts.now ?? (() => performance.now());
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedBundle> {
            let frameCount = 0;
            let lastKeyframeAtMs = Number.NEGATIVE_INFINITY;
            for await (const bundle of source) {
                let mustClose = true;
                try {
                    frameCount++;
                    const wallNow = now();
                    const upstreamForce = bundle.layers.some(f => f.forceKeyframe);
                    const intervalTrigger = frameCount % keyframeIntervalFrames === 0;
                    const wallclockTrigger = maxKeyframeIntervalMs !== undefined
                        && lastKeyframeAtMs !== Number.NEGATIVE_INFINITY
                        && (wallNow - lastKeyframeAtMs) >= maxKeyframeIntervalMs;
                    const forceKeyframe = upstreamForce || intervalTrigger || wallclockTrigger;
                    if (forceKeyframe) {
                        frameCount = 0;
                        lastKeyframeAtMs = wallNow;
                    }
                    if (forceKeyframe && !upstreamForce) {
                        const output: CapturedBundle = {
                            ...bundle,
                            layers: bundle.layers.map(withForceKeyframe),
                        };
                        mustClose = false;
                        yield output;
                    } else {
                        mustClose = false;
                        yield bundle;
                    }
                } finally {
                    if (mustClose)
                        closeBundleLayers(bundle);
                }
            }
        }
    };
}

function withForceKeyframe(layer: CapturedFrame): CapturedFrame {
    if (layer.forceKeyframe) return layer;
    return { ...layer, forceKeyframe: true };
}

function closeBundleLayers(bundle: CapturedBundle): void {
    for (const layer of bundle.layers) {
        try { layer.frame.close(); } catch { /* ignore */ }
    }
}
