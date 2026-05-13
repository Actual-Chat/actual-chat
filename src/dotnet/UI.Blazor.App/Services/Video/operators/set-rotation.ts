import { from, type PipeOperator } from 'ix-ext';
import { DeviceOrientation, normalizeRotationQuarter, type RotationQuarter } from 'orientation';
import type { CapturedFrame } from '../frame-envelopes';
import { iosCameraRotationDeg } from '../orientation/quantize';
import { RotationDebouncer } from '../orientation/rotation-debouncer';

export interface SetRotationOptions {
    // 0 = Camera, 1 = ScreenCast — matches .NET VideoSourceKind.
    sourceKind: number;
    // true if the active camera is the front/user-facing one. Captured at
    // recorder start; camera-switch already restarts the recorder.
    isFrontCamera: boolean;
    // Stable across calls to keep dwell tracking continuous.
    debouncer: RotationDebouncer;
    isIos: boolean;
    now?: () => number;
}

const CAMERA_KIND = 0;

// Stamps every CapturedFrame with a quarter-turn `rotation` tag. Does NOT
// transform pixels — receivers apply the rotation at present time.
//
// Priority chain:
//   1. frame.rotation if non-null and finite (Android Chrome, desktop).
//   2. Else if sourceKind === Camera && iOS →
//      iosCameraRotationDeg(DeviceOrientation.current * 90, isFront).
//   3. Else → DeviceOrientation.current (debug overrides land here on
//      desktop where frame.rotation is reliably 0).
//
// `DeviceOrientation` is hydrated in this worker realm via SharedSettings
// — the main thread publishes pose/debug-override updates and the
// `SharedSettingsWorkerSync` relays them to every worker.
export function setRotation(opts: SetRotationOptions): PipeOperator<CapturedFrame, CapturedFrame> {
    const { sourceKind, isFrontCamera, debouncer, isIos } = opts;
    const now = opts.now ?? ((): number => performance.now());
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<CapturedFrame> {
            for await (const envelope of source) {
                let mustClose = true;
                try {
                    const target = pickTarget(envelope.frame);
                    const committed = debouncer.feed(target, now());
                    const forceKeyframe = envelope.forceKeyframe || debouncer.justChanged;
                    if (committed !== 0) {
                        try {
                            (envelope.frame as VideoFrame & { rotation?: number }).rotation = committed * 90;
                        } catch { /* hint only, not load-bearing */ }
                    }
                    const output: CapturedFrame = {
                        ...envelope,
                        rotation: committed,
                        forceKeyframe,
                    };
                    mustClose = false;
                    yield output;
                } finally {
                    if (mustClose)
                        try { envelope.frame.close(); } catch { /* ignore */ }
                }
            }
        }

        function pickTarget(frame: VideoFrame): RotationQuarter {
            const deviceQuarter = DeviceOrientation.current;
            const raw = (frame as VideoFrame & { rotation?: number | null }).rotation;
            const haveFrameRotation = typeof raw === 'number' && Number.isFinite(raw) && raw !== 0;
            if (sourceKind === CAMERA_KIND && isIos) {
                return normalizeRotationQuarter(
                    iosCameraRotationDeg(deviceQuarter * 90, isFrontCamera) / 90);
            }
            if (haveFrameRotation)
                return normalizeRotationQuarter(raw / 90);
            // Trust DeviceOrientation as the canonical source on all other
            // paths — includes debug overrides on desktop and the
            // OS-rotation-locked phone case where screen.orientation is
            // stale but device-motion (or debug) reflects the truth.
            return normalizeRotationQuarter(deviceQuarter);
        }
    };
}
