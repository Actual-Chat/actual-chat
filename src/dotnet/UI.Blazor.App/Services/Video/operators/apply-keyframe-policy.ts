import { from, type PipeOperator } from 'ix-ext';
import type { CapturedBundle, CapturedFrame } from '../frame-envelopes';

export interface KeyframePolicyOptions {
    // Counter resets on every triggered keyframe regardless of trigger reason.
    keyframeIntervalFrames: number;
    maxKeyframeIntervalMs?: number;
    now?: () => number;
    consumeForceKeyframe?: () => boolean;
}

// Sets forceKeyframe across every layer in a bundle on any of:
// frame-count interval, wallclock floor, or upstream-raised flag.
// Shallow-clones layer envelopes when raising the flag.
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
                    const requestedForce = opts.consumeForceKeyframe?.() ?? false;
                    const intervalTrigger = frameCount % keyframeIntervalFrames === 0;
                    // Keepalive re-emissions carry unchanged pixels — a periodic
                    // intra refresh of them is pure bitrate waste, so the wallclock
                    // floor is suppressed; the frame counter still bounds the GOP
                    // and PLI covers on-demand needs.
                    const isKeepAliveBundle = bundle.layers.some(f => f.isKeepAlive);
                    const wallClockTrigger = !isKeepAliveBundle
                        && maxKeyframeIntervalMs !== undefined
                        && lastKeyframeAtMs !== Number.NEGATIVE_INFINITY
                        && (wallNow - lastKeyframeAtMs) >= maxKeyframeIntervalMs;
                    const forceKeyframe = upstreamForce || requestedForce || intervalTrigger || wallClockTrigger;
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
