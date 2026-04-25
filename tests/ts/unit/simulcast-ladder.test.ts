import { describe, it, expect } from 'vitest';
import { hasHigherTopTier, type LadderTier } from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/simulcast-ladder';

const tier = (width: number, height: number, bitrate = 1_000_000): LadderTier =>
    ({ width, height, bitrate });

describe('hasHigherTopTier (Bug N — ladder persistence)', () => {
    it('returns false when incoming top equals existing top', () => {
        const ladder = [tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(ladder, ladder)).toBe(false);
    });

    it('returns true when incoming top is taller than existing top', () => {
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        expect(hasHigherTopTier(incoming, existing)).toBe(true);
    });

    it('returns false when incoming top is shorter than existing top', () => {
        // The exact regression: JS-side promoted to 1080p, C# pushes its
        // hardcoded 720p ladder. Should NOT replace.
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(incoming, existing)).toBe(false);
    });

    it('returns false when existing has same height but more layers (length-only difference)', () => {
        // Same top, fewer base tiers. Caller must rely on the length comparison
        // separately; this helper only watches the top.
        const existing = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const incoming = [tier(640, 360), tier(1280, 720)];
        expect(hasHigherTopTier(incoming, existing)).toBe(false);
    });

    it('returns false on empty ladders', () => {
        expect(hasHigherTopTier([], [tier(1280, 720)])).toBe(false);
        expect(hasHigherTopTier([tier(1280, 720)], [])).toBe(false);
        expect(hasHigherTopTier([], [])).toBe(false);
    });
});
