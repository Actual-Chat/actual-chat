import { describe, it, expect } from 'vitest';
import {
    PullAnimation,
    clampSettleTangent,
    defaultPullAnimationSettings,
    hermite,
    projectRestRatio,
    settleDurationMs,
} from 'pull-animation';

const settings = defaultPullAnimationSettings;

// Runs the animation forward at a fixed frame interval, returning every sampled ratio.
function run(animation: PullAnimation, fromMs: number, toMs: number, stepMs = 1000 / 60): number[] {
    const ratios: number[] = [];
    for (let t = fromMs + stepMs; t <= toMs; t += stepMs)
        ratios.push(animation.advance(t));
    return ratios;
}

function isMonotone(values: number[], sign: number): boolean {
    for (let i = 1; i < values.length; i++)
        if ((values[i] - values[i - 1]) * sign < -1e-9)
            return false;

    return true;
}

describe('settleDurationMs', () => {
    it('scales with the distance to cover', () => {
        expect(settleDurationMs(1, settings)).toBe(200);
        expect(settleDurationMs(0.5, settings)).toBe(100);
    });

    it('floors at a quarter of the full duration', () => {
        expect(settleDurationMs(0.25, settings)).toBe(50);
        expect(settleDurationMs(0.01, settings)).toBe(50);
        expect(settleDurationMs(0, settings)).toBe(50);
    });

    it('ignores the direction and never exceeds the full duration', () => {
        expect(settleDurationMs(-0.5, settings)).toBe(100);
        expect(settleDurationMs(-2, settings)).toBe(200);
    });
});

describe('hermite', () => {
    it('hits both endpoints exactly', () => {
        expect(hermite(0.2, 1, 0.5, 0)).toBeCloseTo(0.2, 12);
        expect(hermite(0.2, 1, 0.5, 1)).toBeCloseTo(1, 12);
    });

    it('leaves at the given tangent and arrives at rest', () => {
        const m0 = 0.6, eps = 1e-6;
        const startSlope = (hermite(0, 1, m0, eps) - hermite(0, 1, m0, 0)) / eps;
        const endSlope = (hermite(0, 1, m0, 1) - hermite(0, 1, m0, 1 - eps)) / eps;
        expect(startSlope).toBeCloseTo(m0, 4);
        expect(endSlope).toBeCloseTo(0, 4);
    });
});

describe('clampSettleTangent', () => {
    it('keeps a tangent that agrees with the magnet, up to the monotonicity bound', () => {
        expect(clampSettleTangent(0.5, 0.4)).toBe(0.5);
        expect(clampSettleTangent(99, 0.4)).toBeCloseTo(1.2, 12);
        expect(clampSettleTangent(-99, -0.4)).toBeCloseTo(-1.2, 12);
    });

    it('drops a tangent that fights the magnet', () => {
        expect(clampSettleTangent(-2, 0.4)).toBe(0);
        expect(clampSettleTangent(2, -0.4)).toBe(0);
    });

    it('produces a monotone curve at and beyond the bound', () => {
        for (const delta of [0.3, -0.3]) {
            const m0 = clampSettleTangent(delta * 1000, delta);
            const values: number[] = [];
            for (let s = 0; s <= 1.0001; s += 0.01)
                values.push(hermite(0.5, 0.5 + delta, m0, Math.min(s, 1)));
            expect(isMonotone(values, Math.sign(delta))).toBe(true);
        }
    });
});

describe('projectRestRatio', () => {
    it('returns the current ratio when at rest', () => {
        expect(projectRestRatio(0.3, 0, settings.deceleration)).toBe(0.3);
    });

    it('projects forward along the direction of travel and stays in 0..1', () => {
        expect(projectRestRatio(0.3, 1, settings.deceleration)).toBe(1);
        expect(projectRestRatio(0.7, -1, settings.deceleration)).toBe(0);
    });
});

describe('PullAnimation follow phase', () => {
    it('eases toward the finger rather than snapping to it', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(1);
        const first = a.advance(1000 / 60);
        expect(first).toBeGreaterThan(0);
        expect(first).toBeLessThan(1);
    });

    it('keeps moving on frames that carry no new touch sample', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(0.5);
        const step = 1000 / 60;
        const r1 = a.advance(step);
        const r2 = a.advance(step * 2);
        const r3 = a.advance(step * 3);
        expect(r2).toBeGreaterThan(r1);
        expect(r3).toBeGreaterThan(r2);
    });

    it('converges to the target and is frame-rate independent', () => {
        const at = (stepMs: number) => {
            const a = new PullAnimation(0, 0);
            a.setTarget(1);
            run(a, 0, 200, stepMs);
            return a.ratio;
        };
        expect(at(1000 / 60)).toBeCloseTo(1, 3);
        // 120Hz must land in the same place as 60Hz, not twice as far along
        expect(at(1000 / 120)).toBeCloseTo(at(1000 / 60), 3);
        expect(at(1000 / 30)).toBeCloseTo(at(1000 / 60), 2);
    });

    it('does not kink the velocity when the finger target steps', () => {
        // A first-order low-pass would jump straight to (target - ratio)/tau here; a second-order
        // one carries its velocity through. That difference is the stutter under the finger.
        const a = new PullAnimation(0, 0);
        a.setTarget(0.3);
        run(a, 0, 60);
        a.advance(60); // sync the clock, so the step below really is an instant
        const before = a.velocity;
        expect(before).toBeGreaterThan(0);
        a.setTarget(0.9);
        a.advance(60 + 1e-6);
        // A first-order low-pass would land near (0.9 - ratio)/tau here, tens of times larger
        expect(Math.abs(a.velocity - before)).toBeLessThan(1e-3);
    });

    it('moves more evenly than a first-order filter under irregular touch sampling', () => {
        // Touch samples arriving at uneven intervals is the real-device case the spring exists for.
        const cadence = [8, 30, 12, 26, 9, 33, 14, 22, 10, 28, 16, 24];
        const frameMs = 1000 / 60;
        const samples: { at: number; target: number }[] = [];
        let at = 0, target = 0;
        for (const gap of cadence) {
            at += gap;
            target = Math.min(1, target + 0.06);
            samples.push({ at, target });
        }
        const endMs = at;

        // Jerk in absolute terms, not relative to the filter's own baseline: a smoother filter has a
        // much smaller median step, which would make any max/median ratio punish it for being good.
        const roughness = (step: (dtMs: number, target: number) => number) => {
            const ratios: number[] = [];
            let next = 0, current = 0;
            for (let t = frameMs; t <= endMs; t += frameMs) {
                while (next < samples.length && samples[next].at <= t) current = samples[next++].target;
                ratios.push(step(frameMs, current));
            }
            const velocity = ratios.slice(1).map((r, i) => (r - ratios[i]) / frameMs);
            const steps = velocity.slice(1).map((v, i) => Math.abs(v - velocity[i]));
            return {
                maxJerk: Math.max(...steps),
                totalVariation: steps.reduce((sum, s) => sum + s, 0),
                lag: current - ratios[ratios.length - 1],
            };
        };

        const animation = new PullAnimation(0, 0);
        let now = 0;
        const spring = roughness((dtMs, t) => {
            animation.setTarget(t);
            now += dtMs;
            return animation.advance(now);
        });
        // The first-order low-pass this replaced, at the tau it shipped with
        let x = 0;
        const firstOrder = roughness((dtMs, t) => {
            x += (t - x) * (1 - Math.exp(-dtMs / 25));
            return x;
        });
        expect(spring.maxJerk).toBeLessThan(firstOrder.maxJerk);
        expect(spring.totalVariation).toBeLessThan(firstOrder.totalVariation);
        // ...and it buys that without trailing the finger any further
        expect(spring.lag).toBeLessThanOrEqual(firstOrder.lag);
    });

    it('never overshoots a stop while following', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(1);
        const ratios = run(a, 0, 400);
        expect(Math.max(...ratios)).toBeLessThanOrEqual(1);
        expect(Math.min(...ratios)).toBeGreaterThanOrEqual(0);
    });

    it('ignores a target set after release', () => {
        const a = new PullAnimation(0.4, 0);
        a.release(10, 0);
        a.setTarget(1);
        expect(a.phase).toBe('settle');
        expect(a.terminalRatio).toBe(0);
    });
});

describe('PullAnimation settle phase', () => {
    it('picks the near end when released at rest', () => {
        const a = new PullAnimation(0.4, 0);
        a.release(0);
        expect(a.terminalRatio).toBe(0);
        const b = new PullAnimation(0.6, 0);
        b.release(0);
        expect(b.terminalRatio).toBe(1);
    });

    it('lets a flick carry it past the halfway point', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(1);
        a.advance(1000 / 60);
        // Released short of halfway, but still moving toward the far end - the projection decides
        expect(a.ratio).toBeLessThan(0.5);
        expect(a.velocity).toBeGreaterThan(0);
        a.release(1000 / 60);
        expect(a.terminalRatio).toBe(1);
    });

    it('does not carry past halfway when the finger stopped before lifting', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(0.4);
        run(a, 0, 300);
        expect(a.ratio).toBeCloseTo(0.4, 3);
        expect(a.velocity).toBeCloseTo(0, 3);
        a.release(300);
        expect(a.terminalRatio).toBe(0);
    });

    it('honours an explicit terminal on a cancelled gesture', () => {
        const a = new PullAnimation(0, 0);
        a.setTarget(1);
        run(a, 0, 60);
        a.release(60, 0);
        expect(a.terminalRatio).toBe(0);
    });

    it('arrives exactly, and at rest, at the computed duration', () => {
        const a = new PullAnimation(0.5, 0);
        a.release(0, 1);
        const duration = settleDurationMs(0.5, settings);
        expect(duration).toBe(100);
        expect(a.advance(duration - 1)).toBeLessThan(1);
        expect(a.advance(duration)).toBe(1);
        expect(a.isDone).toBe(true);
        expect(a.velocity).toBe(0);
    });

    it('arrives within one frame of the duration when stepped by frames', () => {
        const a = new PullAnimation(0.5, 0);
        a.release(0, 1);
        const step = 1000 / 60;
        run(a, 0, settleDurationMs(0.5, settings) + step, step);
        expect(a.isDone).toBe(true);
        expect(a.ratio).toBe(1);
    });

    it('never overshoots the stop, even released at high speed', () => {
        const a = new PullAnimation(0.05, 0);
        a.setTarget(1);
        run(a, 0, 120);
        a.release(120);
        const ratios = run(a, 120, 120 + settings.settleDurationMs);
        expect(Math.max(...ratios)).toBeLessThanOrEqual(1);
        expect(isMonotone(ratios, 1)).toBe(true);
        expect(a.ratio).toBe(1);
    });

    it('completes immediately when released at its terminal', () => {
        const a = new PullAnimation(1, 0);
        a.release(0, 1);
        expect(a.isDone).toBe(true);
        expect(a.ratio).toBe(1);
    });

    it('stays put once done', () => {
        const a = new PullAnimation(0.5, 0);
        a.release(0, 1);
        run(a, 0, 500);
        expect(a.ratio).toBe(1);
        expect(a.advance(10000)).toBe(1);
    });
});
