import { from, type PipeOperator } from 'ix-ext';
import type { CapturedFrame, SimulcastBundle } from '../frame-envelopes';

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
 * Sets `forceKeyframe = true` across a bundle on any of: frame-count
 * interval, wallclock floor, or upstream already raised the flag.
 * Sets the flag on the bundle, primary, and every extra so downstream
 * encoders can consult any view.
 *
 * Shallow-clones the layer envelopes when raising the flag so upstream
 * operators see no surprising mutations.
 */
export function applyKeyframePolicy(opts: KeyframePolicyOptions): PipeOperator<SimulcastBundle, SimulcastBundle> {
    const { keyframeIntervalFrames, maxKeyframeIntervalMs } = opts;
    if (keyframeIntervalFrames <= 0)
        throw new Error('applyKeyframePolicy: keyframeIntervalFrames must be > 0');
    const now = opts.now ?? (() => performance.now());
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<SimulcastBundle> {
            let frameCount = 0;
            let lastKeyframeAtMs = Number.NEGATIVE_INFINITY;
            for await (const bundle of source) {
                let mustClose = true;
                try {
                    frameCount++;
                    const wallNow = now();
                    const upstreamForce = bundle.primary.forceKeyframe;
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
                        const output = {
                            ...bundle,
                            primary: withForceKeyframe(bundle.primary),
                            extras: bundle.extras.map(withForceKeyframe),
                        };
                        mustClose = false;
                        yield output;
                    } else {
                        mustClose = false;
                        yield bundle;
                    }
                } finally {
                    if (mustClose)
                        closeBundleFrames(bundle);
                }
            }
        }
    };
}

function withForceKeyframe(layer: CapturedFrame): CapturedFrame {
    if (layer.forceKeyframe) return layer;
    return { ...layer, forceKeyframe: true };
}

function closeBundleFrames(bundle: SimulcastBundle): void {
    for (const layer of [...bundle.extras, bundle.primary]) {
        try { layer.frame.close(); } catch { /* ignore */ }
    }
}
