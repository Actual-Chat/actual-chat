import { describe, it, expect } from 'vitest';
import { RotationDebouncer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/orientation/rotation-debouncer';

describe('RotationDebouncer', () => {
    it('returns initial value with no feed', () => {
        const d = new RotationDebouncer(0, 200);
        expect(d.committed).toBe(0);
        expect(d.justChanged).toBe(false);
    });

    it('commits a stable new value after dwell', () => {
        const d = new RotationDebouncer(0, 200);
        expect(d.feed(1, 0)).toBe(0);
        expect(d.feed(1, 100)).toBe(0);
        expect(d.feed(1, 200)).toBe(1);
        expect(d.justChanged).toBe(true);
    });

    it('justChanged clears on next feed', () => {
        const d = new RotationDebouncer(0, 200);
        d.feed(1, 0);
        d.feed(1, 200);
        expect(d.justChanged).toBe(true);
        d.feed(1, 250);
        expect(d.justChanged).toBe(false);
    });

    it('rejects flap: candidate restarts when target changes', () => {
        const d = new RotationDebouncer(0, 200);
        d.feed(1, 0);
        d.feed(1, 150);     // dwell not yet complete
        d.feed(2, 160);     // new candidate restarts
        expect(d.feed(2, 250)).toBe(0); // 90ms < dwell → still 0
        expect(d.feed(2, 360)).toBe(2); // 200ms reached → commit
        expect(d.justChanged).toBe(true);
    });

    it('returning to committed value cancels candidate', () => {
        const d = new RotationDebouncer(0, 200);
        d.feed(1, 0);
        d.feed(1, 100);
        d.feed(0, 150);     // back to committed → reset
        d.feed(1, 200);     // fresh candidate
        expect(d.feed(1, 399)).toBe(0); // 199ms < dwell
        expect(d.feed(1, 400)).toBe(1);
    });

    it('same-target feeds keep commit point at first sighting', () => {
        const d = new RotationDebouncer(0, 200);
        d.feed(1, 100);
        d.feed(1, 150);
        d.feed(1, 200);
        d.feed(1, 250);
        expect(d.feed(1, 300)).toBe(1);
    });

    it('non-zero initial value', () => {
        const d = new RotationDebouncer(2, 200);
        expect(d.committed).toBe(2);
        d.feed(0, 0);
        expect(d.feed(0, 200)).toBe(0);
    });
});
