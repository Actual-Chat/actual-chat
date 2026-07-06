import { describe, it, expect } from 'vitest';
import {
    computeTargetFps,
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
