import { describe, expect, it } from 'vitest';
import {
    pickRenderBackendKind,
    readRenderBackendOverride,
} from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/render-backend-selection';

describe('render backend selection', () => {
    it('reads renderBackend query override', () => {
        expect(readRenderBackendOverride('https://local.test/?renderBackend=canvas')).toBe('canvas');
        expect(readRenderBackendOverride('https://local.test/?renderBackend=mstg')).toBe('mstg');
        expect(readRenderBackendOverride('https://local.test/?renderBackend=wat')).toBeNull();
        expect(readRenderBackendOverride('https://local.test/')).toBeNull();
    });

    it('uses override before browser plausibility', () => {
        expect(pickRenderBackendKind('canvas', true)).toBe('canvas');
        expect(pickRenderBackendKind('canvas', false)).toBe('canvas');
        expect(pickRenderBackendKind('mstg', true)).toBe('mstg');
        expect(pickRenderBackendKind('mstg', false)).toBe('mstg');
    });

    it('defaults to mstg only when plausible', () => {
        expect(pickRenderBackendKind(null, true)).toBe('mstg');
        expect(pickRenderBackendKind(null, false)).toBe('canvas');
    });
});
