import { describe, it, expect } from 'vitest';
import { pipe, count } from 'ix-ext';
import {
    MutableWireGate,
    wireGate,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/operators/wire-gate';
import type { EncodedBundle, EncodedFrame } from
    '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

function makeBundle(index: number, onClose: () => void): EncodedBundle {
    const chunk = { close: onClose } as unknown as EncodedVideoChunk;
    const frame = { chunk } as unknown as EncodedFrame;
    return {
        layers: [frame],
        index,
        dropTrace: [],
        rotation: 0,
        stats: {} as EncodedBundle['stats'],
    };
}

describe('wireGate', () => {
    it('forwards bundles when open', async () => {
        const gate = new MutableWireGate(true);
        let closed = 0;
        const bundles = [
            makeBundle(0, () => closed++),
            makeBundle(1, () => closed++),
            makeBundle(2, () => closed++),
        ];
        // eslint-disable-next-line @typescript-eslint/require-await
        async function* src(): AsyncIterable<EncodedBundle> {
            for (const b of bundles) yield b;
        }
        const out = pipe(src(), wireGate(gate));
        expect(await count(out)).toBe(3);
        expect(closed).toBe(0);
    });

    it('drops + disposes bundles when closed', async () => {
        const gate = new MutableWireGate(false);
        let closed = 0;
        const bundles = [
            makeBundle(0, () => closed++),
            makeBundle(1, () => closed++),
            makeBundle(2, () => closed++),
        ];
        // eslint-disable-next-line @typescript-eslint/require-await
        async function* src(): AsyncIterable<EncodedBundle> {
            for (const b of bundles) yield b;
        }
        const out = pipe(src(), wireGate(gate));
        expect(await count(out)).toBe(0);
        expect(closed).toBe(3);
    });

    it('honors flips mid-stream', async () => {
        const gate = new MutableWireGate(false);
        let closed = 0;
        const bundles = [
            makeBundle(0, () => closed++), // drop
            makeBundle(1, () => closed++), // open before yield: forward
            makeBundle(2, () => closed++), // forward
            makeBundle(3, () => closed++), // close before yield: drop
        ];
        // eslint-disable-next-line @typescript-eslint/require-await
        async function* src(): AsyncIterable<EncodedBundle> {
            yield bundles[0];
            gate.setOpen(true);
            yield bundles[1];
            yield bundles[2];
            gate.setOpen(false);
            yield bundles[3];
        }
        const out = pipe(src(), wireGate(gate));
        const collected: EncodedBundle[] = [];
        for await (const b of out) collected.push(b);
        expect(collected.map(b => b.index)).toEqual([1, 2]);
        expect(closed).toBe(2);
    });

});
