import { describe, it, expect } from 'vitest';
import {
    hasHigherTopTier,
    maybeAugmentLadderForRunningBase,
    type LadderTier,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/simulcast-ladder';

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

describe('maybeAugmentLadderForRunningBase (Bug AA — wire-id monotonicity)', () => {
    it('appends a tier matching running base when running is taller than top', () => {
        // The exact regression: pipeline is in single-encoder 1080p (JS-promoted).
        // C# pushes a 720p-cap ladder to activate simulcast. Without augmentation
        // the wire becomes spatial=0=1080p (running base), 1=360p, 2=720p — the
        // server filter (which sorts by spatial-id) picks spatial=2=720p instead
        // of the 1080p baked into spatial=0.
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const running = { width: 1920, height: 1080, bitrate: 2_300_000 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).toHaveLength(4);
        expect(result[3]).toEqual({ width: 1920, height: 1080, bitrate: 2_300_000 });
    });

    it('returns input unchanged when running base equals top tier', () => {
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        const running = { width: 1920, height: 1080, bitrate: 2_300_000 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).toEqual(incoming);
        expect(result).toHaveLength(4);
    });

    it('returns input unchanged when running base is shorter than top tier', () => {
        // Pipeline reconfigured down to 720p mid-stream; C# pushes a 1080p-top
        // ladder. No augmentation needed — top is already biggest.
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720), tier(1920, 1080)];
        const running = { width: 1280, height: 720, bitrate: 2_000_000 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).toEqual(incoming);
    });

    it('returns input unchanged when runningBase is null', () => {
        // No active pipeline / no encoder stats yet.
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];

        const result = maybeAugmentLadderForRunningBase(incoming, null);

        expect(result).toEqual(incoming);
    });

    it('returns input unchanged when runningBase has zero height', () => {
        // Pipeline still warming — getEncoderStats not yet populated.
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const running = { width: 0, height: 0, bitrate: 0 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).toEqual(incoming);
    });

    it('returns a copy (does not mutate input)', () => {
        const incoming = [tier(320, 180), tier(640, 360), tier(1280, 720)];
        const running = { width: 1920, height: 1080, bitrate: 2_300_000 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).not.toBe(incoming);
        expect(incoming).toHaveLength(3);
    });

    it('returns a copy on no-op path too', () => {
        const incoming = [tier(1920, 1080)];
        const running = { width: 1920, height: 1080, bitrate: 2_300_000 };

        const result = maybeAugmentLadderForRunningBase(incoming, running);

        expect(result).not.toBe(incoming);
        expect(result).toEqual(incoming);
    });

    it('handles empty incoming ladder', () => {
        const result = maybeAugmentLadderForRunningBase([], { width: 1920, height: 1080, bitrate: 2_300_000 });
        expect(result).toEqual([]);
    });
});
