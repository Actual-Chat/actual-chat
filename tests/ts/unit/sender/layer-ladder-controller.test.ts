import { describe, it, expect } from 'vitest';
import { LayerLadderController } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/sender/layer-ladder-controller';
import type { EncoderConfigPerLayer } from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encode';

const cfg = (width: number, height: number): EncoderConfigPerLayer => ({
    width, height,
    bitrate: 500_000,
    framerate: 30,
    codec: 'avc1.42E01E',
});

describe('LayerLadderController', () => {
    it('exposes the initial configs at version 0', () => {
        const c = new LayerLadderController([cfg(320, 180), cfg(1280, 720)]);
        expect(c.current.version).toBe(0);
        expect(c.current.configs).toHaveLength(2);
        expect(c.current.configs[0].width).toBe(320);
        expect(c.current.configs[1].width).toBe(1280);
    });

    it('bumps version monotonically on setConfigs', () => {
        const c = new LayerLadderController([cfg(640, 360)]);
        const v1 = c.setConfigs([cfg(640, 360), cfg(1280, 720)]);
        const v2 = c.setConfigs([cfg(1280, 720)]);
        expect(v1).toBe(1);
        expect(v2).toBe(2);
        expect(c.current.version).toBe(2);
        expect(c.current.configs).toHaveLength(1);
    });

    it('takes a defensive slice on construction so caller mutation does not leak in', () => {
        const initial = [cfg(640, 360), cfg(1280, 720)];
        const c = new LayerLadderController(initial);
        initial.push(cfg(1920, 1080));
        expect(c.current.configs).toHaveLength(2);
    });

    it('takes a defensive slice on setConfigs', () => {
        const c = new LayerLadderController([cfg(640, 360)]);
        const next = [cfg(640, 360), cfg(1280, 720)];
        c.setConfigs(next);
        next.length = 0;
        expect(c.current.configs).toHaveLength(2);
    });

    it('rejects empty configs at construction', () => {
        expect(() => new LayerLadderController([])).toThrow();
    });

    it('rejects empty configs at setConfigs', () => {
        const c = new LayerLadderController([cfg(640, 360)]);
        expect(() => c.setConfigs([])).toThrow();
        expect(c.current.version).toBe(0);
    });
});
