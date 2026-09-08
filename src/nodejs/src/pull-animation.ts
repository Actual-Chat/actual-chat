// The motion model behind a finger-driven panel pull, on a 0..1 axis. Pure: it owns no DOM and no
// clock, so the caller feeds it timestamps and it is exercised end to end by unit tests.
//
// Two phases. While the finger is down the value chases it through a critically damped spring -
// touch samples do not arrive one per frame, and interpolating between them is what keeps the panel
// moving on the frames that carry no sample. When the finger lifts, a magnet takes over and pulls
// the value to whichever end the motion was heading for.
//
// The spring is second order on purpose. Samples land irregularly, so the target moves in steps,
// and against a second-order filter a step changes acceleration rather than velocity. A first-order
// low-pass instead kinks the velocity on every sample, and that reads as stutter under the finger
// even when no frame is dropped - which is what made the drag feel worse than the settle, since the
// settle was always a smooth closed-form curve.

import { clamp } from 'math';

export interface PullAnimationSettings {
    /** Time constant of the finger-following spring; the lag behind a moving finger is twice it. */
    followTimeConstantMs: number;
    /** Settle duration for a magnet that has to cross the whole axis. */
    settleDurationMs: number;
    /** Floor on the settle duration, as a fraction of settleDurationMs. */
    minSettleDurationRatio: number;
    /** Deceleration used to project where the current motion would come to rest, in 1/s^2. */
    deceleration: number;
}

export const defaultPullAnimationSettings: PullAnimationSettings = {
    // A spring lags 2x its time constant, so this tracks the finger like the 25ms first-order
    // low-pass it replaced - the smoothness comes from the order, not from more lag
    followTimeConstantMs: 12,
    settleDurationMs: 200,
    minSettleDurationRatio: 0.25,
    deceleration: 0.1,
};

export type PullAnimationPhase = 'follow' | 'settle' | 'done';

/** Where a motion at `velocity` from `ratio` comes to rest under constant deceleration. */
export function projectRestRatio(ratio: number, velocity: number, deceleration: number): number {
    if (velocity === 0 || deceleration <= 0)
        return clamp(ratio, 0, 1);

    const decelerationTime = Math.abs(velocity / deceleration);
    return clamp(ratio + velocity * decelerationTime / 2, 0, 1);
}

export function settleDurationMs(distance: number, settings: PullAnimationSettings): number {
    const span = Math.min(1, Math.abs(distance));
    return settings.settleDurationMs * Math.max(settings.minSettleDurationRatio, span);
}

// A cubic Hermite from `from` to `to` over s in 0..1, entering at tangent m0 and leaving at rest.
// m0 is expected pre-clamped by clampSettleTangent.
export function hermite(from: number, to: number, m0: number, s: number): number {
    const delta = to - from;
    return from + delta * s * s * (3 - 2 * s) + m0 * s * (s - 1) * (s - 1);
}

// With a zero end tangent the Hermite stays monotone exactly while m0 is between 0 and 3*delta:
// at m0 = 3*delta its derivative is 3*delta*(s-1)^2, and past that the derivative changes sign near
// s = 1 and the value overshoots the stop. So a flick that agrees with the magnet is honoured up to
// that bound, and one that fights it is dropped rather than bounced.
export function clampSettleTangent(m0: number, delta: number): number {
    const bound = 3 * delta;
    return bound >= 0 ? clamp(m0, 0, bound) : clamp(m0, bound, 0);
}

export class PullAnimation {
    private readonly settings: PullAnimationSettings;
    private _ratio: number;
    private _velocity = 0;
    private _phase: PullAnimationPhase = 'follow';
    private targetRatio: number;
    private lastTimeMs: number;
    private settleFrom = 0;
    private settleTo = 0;
    private settleTangent = 0;
    private settleStartedAt = 0;
    private settleDuration = 0;

    /** 0 = fully closed, 1 = fully open. */
    public get ratio(): number { return this._ratio; }
    /** Signed rate of `ratio`, per second. */
    public get velocity(): number { return this._velocity; }
    public get phase(): PullAnimationPhase { return this._phase; }
    public get isDone(): boolean { return this._phase === 'done'; }
    /** The end the magnet is pulling to; meaningless before release(). */
    public get terminalRatio(): number { return this.settleTo; }
    /** Where the current motion would come to rest if nothing stopped it. */
    public get restRatio(): number {
        return projectRestRatio(this._ratio, this._velocity, this.settings.deceleration);
    }

    constructor(ratio: number, timeMs: number, settings?: Partial<PullAnimationSettings>) {
        this.settings = { ...defaultPullAnimationSettings, ...settings };
        this._ratio = clamp(ratio, 0, 1);
        this.targetRatio = this._ratio;
        this.lastTimeMs = timeMs;
    }

    /** Where the finger is now. Ignored once released. */
    public setTarget(ratio: number): void {
        if (this._phase !== 'follow')
            return;

        this.targetRatio = clamp(ratio, 0, 1);
    }

    // Passing terminalRatio overrides the end the motion was heading for - that's a cancelled
    // gesture, which returns to where it started however it was moving.
    public release(timeMs: number, terminalRatio?: number): void {
        if (this._phase !== 'follow')
            return;

        this.advance(timeMs);
        const to = terminalRatio ?? (this.restRatio > 0.5 ? 1 : 0);
        const delta = to - this._ratio;
        this.settleFrom = this._ratio;
        this.settleTo = to;
        this.settleDuration = settleDurationMs(delta, this.settings);
        this.settleTangent = clampSettleTangent(this._velocity * this.settleDuration / 1000, delta);
        this.settleStartedAt = timeMs;
        this._phase = 'settle';
        if (Math.abs(delta) < 1e-6 || this.settleDuration <= 0)
            this.finish();
    }

    /** Advances to `timeMs` and returns the new ratio. */
    public advance(timeMs: number): number {
        const dtMs = timeMs - this.lastTimeMs;
        this.lastTimeMs = timeMs;
        if (this._phase === 'done' || !(dtMs > 0))
            return this._ratio;

        return this._phase === 'follow' ? this.advanceFollow(dtMs) : this.advanceSettle(timeMs, dtMs);
    }

    // Private methods

    // Integrated exactly rather than stepped, so the path is identical at any frame rate
    private advanceFollow(dtMs: number): number {
        const dt = dtMs / 1000;
        const omega = 1000 / this.settings.followTimeConstantMs;
        const offset = this._ratio - this.targetRatio;
        const decay = Math.exp(-omega * dt);
        const c = this._velocity + omega * offset;
        this._ratio = this.targetRatio + (offset + c * dt) * decay;
        this._velocity = (this._velocity - c * omega * dt) * decay;
        const bounded = clamp(this._ratio, 0, 1);
        if (bounded !== this._ratio) {
            // The panel has run into a stop, so it stops rather than storing the impact
            this._ratio = bounded;
            this._velocity = 0;
        }
        return this._ratio;
    }

    private advanceSettle(timeMs: number, dtMs: number): number {
        const s = clamp((timeMs - this.settleStartedAt) / this.settleDuration, 0, 1);
        if (s >= 1)
            return this.finish();

        const previous = this._ratio;
        this._ratio = hermite(this.settleFrom, this.settleTo, this.settleTangent, s);
        this._velocity = (this._ratio - previous) / dtMs * 1000;
        return this._ratio;
    }

    private finish(): number {
        this._ratio = this.settleTo;
        this._velocity = 0;
        this._phase = 'done';
        return this._ratio;
    }
}
