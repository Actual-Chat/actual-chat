import { describe, it, expect } from 'vitest';
import { renderQualityLevelForWidth } from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/render-quality';

describe('renderQualityLevelForWidth', () => {
    it('returns null when canvas has not laid out yet', () => {
        expect(renderQualityLevelForWidth(0)).toBeNull();
        expect(renderQualityLevelForWidth(-1)).toBeNull();
    });

    it('maps tiny widths to Low (level 4)', () => {
        expect(renderQualityLevelForWidth(1)).toBe(4);
        expect(renderQualityLevelForWidth(320)).toBe(4);
        expect(renderQualityLevelForWidth(480)).toBe(4);  // upper boundary
    });

    it('maps multi-tile widths to Medium (level 3)', () => {
        expect(renderQualityLevelForWidth(481)).toBe(3);  // just past Low boundary
        expect(renderQualityLevelForWidth(640)).toBe(3);
        expect(renderQualityLevelForWidth(720)).toBe(3);  // upper boundary
    });

    it('maps tablet/single-tile widths to High (level 2)', () => {
        expect(renderQualityLevelForWidth(721)).toBe(2);
        expect(renderQualityLevelForWidth(960)).toBe(2);
        expect(renderQualityLevelForWidth(1280)).toBe(2); // upper boundary
    });

    it('maps full-screen widths to Full (level 1)', () => {
        expect(renderQualityLevelForWidth(1281)).toBe(1);
        expect(renderQualityLevelForWidth(1600)).toBe(1);
        expect(renderQualityLevelForWidth(1920)).toBe(1); // upper boundary
    });

    it('maps 4K-class widths to Ultra (level 0)', () => {
        expect(renderQualityLevelForWidth(1921)).toBe(0);
        expect(renderQualityLevelForWidth(2560)).toBe(0);
        expect(renderQualityLevelForWidth(3840)).toBe(0);
    });

    it('regression — 2-tile layout at 480 px maps to Low, not High', () => {
        // The bug from the original Round-3 plan: 2-tile video panel with each
        // canvas at ~480 px was nominally giving the right level, but the
        // server saw RenderLvl=2 (High) before this code ran. With the helper
        // pulled out and testable, lock the boundary in.
        expect(renderQualityLevelForWidth(480)).toBe(4);
    });
});
