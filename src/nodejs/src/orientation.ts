import { Subject, Observable } from 'rxjs';
import { DeviceInfo } from 'device-info';
import { SharedSettings } from 'shared-settings';

/** A device / frame rotation expressed as an integer quarter-turn count
 *  CW from natural portrait. Canonical range is `0..3` but values from
 *  any source (raw debugUI input, accumulating loops, deltas) may be
 *  outside it; use {@link normalizeRotationQuarter} to fold them into
 *  the canonical range before stamping wire fields or applying CSS. */
export type RotationQuarter = number;

/** Folds any number (negatives, > 3, fractional) into the canonical
 *  0..3 quarter-turn range. Boundaries are centered on each quarter
 *  (quarter N owns the half-band `[N - 0.5, N + 0.5)`), so 0 covers
 *  the "-45° to 45° around 12 o'clock" arc when callers pass `deg / 90`. */
export function normalizeRotationQuarter(value: RotationQuarter): RotationQuarter {
    if (!Number.isFinite(value)) return 0;
    return (((Math.round(value) % 4) + 4) % 4);
}

// Both classes are safe to import in workers — `init()` no-ops the
// browser-only listeners when `screen` / `window` aren't available.
// In a worker the values stay in sync via the SharedSettings.changed
// callback registered below, which the main thread populates from real
// `screen.orientation` / `deviceorientation` events.

export class ScreenOrientation {
    private static _current = 0;
    private static _isInitialized = false;
    private static readonly _changeSubject = new Subject<number>();

    public static readonly change$: Observable<number> = ScreenOrientation._changeSubject;

    public static get current(): number {
        return ScreenOrientation._current;
    }

    public static get quarter(): RotationQuarter {
        return normalizeRotationQuarter(ScreenOrientation._current / 90);
    }

    public static get isPortrait(): boolean {
        return (ScreenOrientation._current % 180) === 0;
    }

    public static init(): void {
        if (ScreenOrientation._isInitialized)
            return;

        ScreenOrientation._isInitialized = true;
        ScreenOrientation._current = readScreenAngle();
        const screenOrientation = typeof screen !== 'undefined'
            ? (screen as unknown as { orientation?: { addEventListener?: (e: string, cb: () => void) => void } }).orientation
            : undefined;
        const hasLegacyOrientation = typeof window !== 'undefined' && 'orientation' in window;
        const isMain = !!screenOrientation || hasLegacyOrientation;

        if (isMain) {
            const onChange = (): void => ScreenOrientation.commit(readScreenAngle(), true);
            if (screenOrientation && typeof screenOrientation.addEventListener === 'function')
                screenOrientation.addEventListener('change', onChange);
            if (hasLegacyOrientation)
                window.addEventListener('orientationchange', onChange);
            // Seed SharedSettings with the initial value so workers start in sync.
            SharedSettings.update({ screenOrientation: ScreenOrientation._current });
        }

        SharedSettings.changed.add(s => {
            if (typeof s.screenOrientation === 'number')
                ScreenOrientation.commit(s.screenOrientation, false);
        });
    }

    private static commit(angle: number, publish: boolean): void {
        const normalized = normalizeRotationQuarter(angle / 90) * 90;
        if (normalized === ScreenOrientation._current) return;
        ScreenOrientation._current = normalized;
        ScreenOrientation._changeSubject.next(normalized);
        if (publish)
            SharedSettings.update({ screenOrientation: normalized });
    }
}

// Device-pose channel — what the video pipeline rotates by.
//   * Main, non-iOS: subscribes to DeviceOrientationEvent (no permission prompt).
//   * Main, iOS: mirrors ScreenOrientation — no motion permission prompt.
//   * Worker: hydrated solely from SharedSettings.changed.
//   * debugUI: `set()` from any realm pushes through commit → SharedSettings.
export class DeviceOrientation {
    private static _quarter: RotationQuarter = 0;
    private static _angle = 0;
    private static _isInitialized = false;
    private static _isMotionListenerAttached = false;
    private static readonly _changeSubject = new Subject<RotationQuarter>();

    public static readonly change$: Observable<RotationQuarter> = DeviceOrientation._changeSubject;

    public static get quarter(): RotationQuarter {
        return DeviceOrientation._quarter;
    }

    public static get angle(): number {
        return DeviceOrientation._angle;
    }

    // Debug / test override. Pushes a value as if it came from a real sensor
    // or screen-orientation flip. Used by debugUI.setDeviceOrientation.
    public static set(quarter: RotationQuarter): void {
        const normalized = normalizeRotationQuarter(quarter);
        DeviceOrientation.commit(normalized, true, normalized * 90);
    }

    public static init(): void {
        if (DeviceOrientation._isInitialized)
            return;

        DeviceOrientation._isInitialized = true;
        DeviceOrientation._quarter = ScreenOrientation.quarter;
        DeviceOrientation._angle = ScreenOrientation.current;
        const isMain = typeof window !== 'undefined' && typeof screen !== 'undefined';

        if (isMain) {
            ScreenOrientation.change$.subscribe(() => {
                // On iOS or when no motion input is available, we use screen orientation as device orientation
                if (DeviceInfo.isIos || !hasMotionInput)
                    DeviceOrientation.commit(ScreenOrientation.quarter, true, ScreenOrientation.current);
            });
            if (!DeviceInfo.isIos)
                DeviceOrientation.attachMotionListener();
            // Seed SharedSettings so workers start in sync.
            SharedSettings.update({
                deviceOrientation: DeviceOrientation._quarter,
                deviceOrientationAngle: DeviceOrientation._angle,
            });
        }

        SharedSettings.changed.add(s => {
            const hasQuarter = typeof s.deviceOrientation === 'number';
            const hasAngle = typeof s.deviceOrientationAngle === 'number';
            if (hasQuarter || hasAngle) {
                const quarter = hasQuarter
                    ? s.deviceOrientation!
                    : DeviceOrientation._quarter;
                const angle = hasAngle
                    ? s.deviceOrientationAngle!
                    : DeviceOrientation._angle;
                DeviceOrientation.commit(
                    quarter,
                    false,
                    angle);
            }
        });
    }

    private static attachMotionListener(): void {
        if (DeviceOrientation._isMotionListenerAttached)
            return;

        DeviceOrientation._isMotionListenerAttached = true;
        let lastSampleAtMs = 0;
        const minIntervalMs = 100; // throttle ~10 Hz before quantising
        window.addEventListener('deviceorientation', (e: DeviceOrientationEvent) => {
            const now = performance.now();
            if (now - lastSampleAtMs < minIntervalMs) return;
            lastSampleAtMs = now;
            const next = poseToRotationQuarter(e);
            const angle = poseToRotationAngle(e);
            if (next !== null || angle !== null) {
                hasMotionInput = true;
                DeviceOrientation.commit(
                    next ?? DeviceOrientation._quarter,
                    true,
                    angle ?? (next ?? DeviceOrientation._quarter) * 90);
            }
        });
    }

    private static commit(next: RotationQuarter, publish: boolean, angle?: number): void {
        const normalized = normalizeRotationQuarter(next);
        const normalizedAngle = quantizeRotationDegrees(angle ?? normalized * 90);
        if (normalized === DeviceOrientation._quarter
            && normalizedAngle === DeviceOrientation._angle)
            return;

        const quarterChanged = normalized !== DeviceOrientation._quarter;
        DeviceOrientation._quarter = normalized;
        DeviceOrientation._angle = normalizedAngle;
        if (quarterChanged)
            DeviceOrientation._changeSubject.next(normalized);
        if (publish)
            SharedSettings.update({
                deviceOrientation: normalized,
                deviceOrientationAngle: normalizedAngle,
            });
    }
}

// Helpers

let hasMotionInput = false;

function readScreenAngle(): number {
    const legacy = readLegacyWindowOrientation();
    if (DeviceInfo.isIos && legacy !== null)
        return legacy;

    // `screen.orientation` is typed as non-optional but can be missing on
    // older WebKit / some worker realms; check defensively.
    const screenLike = typeof screen !== 'undefined'
        ? (screen as unknown as { orientation?: { angle: number } })
        : undefined;
    const angle = screenLike?.orientation?.angle;
    if (typeof angle === 'number' && Number.isFinite(angle))
        return normalizeRotationQuarter(angle / 90) * 90;

    return legacy ?? 0;
}

function readLegacyWindowOrientation(): number | null {
    if (typeof window === 'undefined')
        return null;

    const legacy = (window as unknown as { orientation?: number }).orientation;
    return typeof legacy === 'number' && Number.isFinite(legacy)
        ? normalizeRotationQuarter(legacy / 90) * 90
        : null;
}

// DeviceOrientationEvent → quarter-turn. beta dominates → portrait;
// gamma dominates → landscape. 30° dead-zone + 10° hysteresis margin
// so a still phone doesn't flap.
function poseToRotationQuarter(e: DeviceOrientationEvent): RotationQuarter | null {
    const beta = e.beta;
    const gamma = e.gamma;
    if (beta === null || gamma === null) return null;

    const absBeta = Math.abs(beta);
    const absGamma = Math.abs(gamma);
    if (absBeta < 30 && absGamma < 30) return null;

    if (absGamma > absBeta + 10) return gamma > 0 ? 1 : 3;
    if (absBeta > absGamma + 10) return beta < -30 ? 2 : 0;
    return null;
}

// DeviceOrientationEvent -> degrees CW from natural portrait, quantized by
// DeviceOrientation.commit before publishing. `atan2(gamma, beta)` matches
// the quarter mapping above: beta+ => 0°, gamma+ => 90°, beta- => 180°,
// gamma- => 270°.
function poseToRotationAngle(e: DeviceOrientationEvent): number | null {
    const beta = e.beta;
    const gamma = e.gamma;
    if (beta === null || gamma === null) return null;

    const absBeta = Math.abs(beta);
    const absGamma = Math.abs(gamma);
    if (absBeta < 30 && absGamma < 30) return null;

    return normalizeRotationDegrees(Math.atan2(gamma, beta) * 180 / Math.PI);
}

function normalizeRotationDegrees(value: number): number {
    if (!Number.isFinite(value)) return 0;
    return ((value % 360) + 360) % 360;
}

function quantizeRotationDegrees(value: number, step = 10): number {
    return normalizeRotationDegrees(Math.round(value / step) * step);
}
