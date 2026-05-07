import { describe, it, expect } from 'vitest';
import {
    pickRenderBackend,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/render-backends';
import type { CanvasImageInterface } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/present-canvas';

const fakeWriter = {} as WritableStreamDefaultWriter<VideoFrame>;
const fakeCtx: CanvasImageInterface = { drawImage: () => { /* no-op */ } };

describe('pickRenderBackend', () => {
    it('prefers MSTG when preferred and writer is provided', () => {
        const cfg = pickRenderBackend({
            preferMstg: true,
            mstgWriter: fakeWriter,
            canvasCtx: fakeCtx,
        });
        expect(cfg.kind).toBe('mstg');
        if (cfg.kind === 'mstg') {
            expect(cfg.writer).toBe(fakeWriter);
        }
    });

    it('falls back to canvas when MSTG writer is missing', () => {
        const cfg = pickRenderBackend({
            preferMstg: true,
            canvasCtx: fakeCtx,
        });
        expect(cfg.kind).toBe('canvas');
        if (cfg.kind === 'canvas') {
            expect(cfg.canvasCtx).toBe(fakeCtx);
            expect(cfg.convertToBitmap).toBeUndefined();
        }
    });

    it('uses canvas when MSTG is not preferred even if writer is supplied', () => {
        const conv = (_frame: VideoFrame): Promise<ImageBitmap> =>
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            Promise.resolve({} as any as ImageBitmap);
        const cfg = pickRenderBackend({
            preferMstg: false,
            mstgWriter: fakeWriter,
            canvasCtx: fakeCtx,
            convertToBitmap: conv,
        });
        expect(cfg.kind).toBe('canvas');
        if (cfg.kind === 'canvas') {
            expect(cfg.canvasCtx).toBe(fakeCtx);
            expect(cfg.convertToBitmap).toBe(conv);
        }
    });

    it('throws when neither MSTG writer nor canvas context is available', () => {
        expect(() => pickRenderBackend({ preferMstg: true })).toThrow(/no rendering surface/);
    });
});
