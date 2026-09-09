import { describe, expect, it } from 'vitest';
import {
    DeadPeerDetector,
    type DeadPeerSample,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/dead-peer-detector';

function sample(over: Partial<DeadPeerSample> = {}): DeadPeerSample {
    return { isPullActive: true, isWorkerConnected: false, isMainConnected: true, ...over };
}

describe('DeadPeerDetector', () => {
    it('reports a dead peer once the worker stays disconnected while the main peer is connected', () => {
        const d = new DeadPeerDetector(15_000);
        expect(d.onSample(sample(), 0)).toBe(false);
        expect(d.onSample(sample(), 10_000)).toBe(false);
        expect(d.onSample(sample(), 15_000)).toBe(true);
    });

    it('re-arms after reporting so a worker that dies again is reported again', () => {
        const d = new DeadPeerDetector(15_000);
        d.onSample(sample(), 0);
        expect(d.onSample(sample(), 15_000)).toBe(true);
        expect(d.onSample(sample(), 20_000)).toBe(false);
        expect(d.onSample(sample(), 35_000)).toBe(true);
    });

    it('resets when the worker reconnects on its own', () => {
        const d = new DeadPeerDetector(15_000);
        d.onSample(sample(), 0);
        d.onSample(sample({ isWorkerConnected: true }), 10_000);
        expect(d.onSample(sample(), 20_000)).toBe(false);
        expect(d.onSample(sample(), 34_000)).toBe(false);
        expect(d.onSample(sample(), 35_000)).toBe(true);
    });

    it('does not count time while the main peer is down or no pull is active', () => {
        const d = new DeadPeerDetector(15_000);
        d.onSample(sample({ isMainConnected: false }), 0);
        d.onSample(sample({ isMainConnected: false }), 20_000);
        expect(d.onSample(sample(), 21_000)).toBe(false);
        d.onSample(sample({ isPullActive: false }), 30_000);
        expect(d.onSample(sample(), 40_000)).toBe(false);
        expect(d.onSample(sample(), 55_000)).toBe(true);
    });
});
