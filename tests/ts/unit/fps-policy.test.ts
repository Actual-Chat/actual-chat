import { describe, it, expect } from 'vitest';
import {
    computeCaptureFps,
    computeTargetFps,
    CAPTURE_SHED_FPS,
    THUMBNAIL_FPS,
    type TargetFpsInputs,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/fps-policy';

const base: TargetFpsInputs = {
    captureFps: 24,
    fpsCeiling: 0,
    thumbnailShedActive: true,
    isSpeaking: false,
    remoteStreamCount: 2,
    isScreencast: false,
};

describe('computeTargetFps', () => {
    it('sheds to THUMBNAIL_FPS only when every gate passes', () => {
        expect(computeTargetFps(base)).toBe(THUMBNAIL_FPS);
    });

    it('speaking always wins over the shed', () => {
        expect(computeTargetFps({ ...base, isSpeaking: true })).toBe(24);
    });

    it('no shed without the server thumbnail aggregate', () => {
        expect(computeTargetFps({ ...base, thumbnailShedActive: false })).toBe(24);
    });

    it('screencast never sheds', () => {
        expect(computeTargetFps({ ...base, isScreencast: true })).toBe(24);
    });

    it('zero remote streams (own preview is the large tile) blocks the shed', () => {
        expect(computeTargetFps({ ...base, remoteStreamCount: 0 })).toBe(24);
    });

    it('thermal ceiling still caps when no shed applies', () => {
        expect(computeTargetFps({ ...base, isSpeaking: true, fpsCeiling: 15 })).toBe(15);
    });

    it('shed composes with the thermal ceiling via min', () => {
        expect(computeTargetFps({ ...base, fpsCeiling: 15 })).toBe(THUMBNAIL_FPS);
        expect(computeTargetFps({ ...base, fpsCeiling: 8 })).toBe(8);
    });

    it('never raises above the capture rate', () => {
        expect(computeTargetFps({ ...base, isSpeaking: true, captureFps: 8 })).toBe(8);
        expect(computeTargetFps({ ...base, captureFps: 8 })).toBe(8);
    });
});

describe('computeCaptureFps', () => {
    it('sheds capture on the thumbnail target', () => {
        expect(computeCaptureFps(THUMBNAIL_FPS, 24)).toBe(CAPTURE_SHED_FPS);
    });

    it('sheds capture on thermal ceilings (Serious=15, Critical=10)', () => {
        expect(computeCaptureFps(15, 24)).toBe(CAPTURE_SHED_FPS);
        expect(computeCaptureFps(10, 30)).toBe(CAPTURE_SHED_FPS);
    });

    it('never undershoots a target between the shed floor and the requested rate', () => {
        expect(computeCaptureFps(20, 24)).toBe(24);
    });

    it('keeps the requested rate at full target', () => {
        expect(computeCaptureFps(24, 24)).toBe(24);
        expect(computeCaptureFps(30, 30)).toBe(30);
    });

    it('never raises above the requested rate', () => {
        expect(computeCaptureFps(10, 12)).toBe(12);
    });
});
