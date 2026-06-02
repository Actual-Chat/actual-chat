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

    it('honors the override', () => {
        expect(pickRenderBackendKind('canvas')).toBe('canvas');
        expect(pickRenderBackendKind('mstg')).toBe('mstg');
    });

    it('defaults to the generator path when no override', () => {
        expect(pickRenderBackendKind(null)).toBe('mstg');
    });
});
