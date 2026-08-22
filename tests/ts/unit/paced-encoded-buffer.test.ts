import { describe, expect, it } from 'vitest';
import { pacedEncodedBuffer } from '../../../src/dotnet/UI.Blazor.App/Services/Video/operators/encoded-buffer';
import { EncodedFrameBuffer } from '../../../src/dotnet/UI.Blazor.App/Services/Video/playback/encoded-frame-buffer';
import type { ArrivedChunk } from '../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

describe('pacedEncodedBuffer', () => {
    it('settles teardown when the consumer stops while upstream keeps delivering', async () => {
        // arrange: a live upstream that never ends, as an RpcStream does
        const source = endlessChunks();
        const iterator = drainOf(source);
        await iterator.next();

        // act: return the consumer, which is what a wedge restart does
        const outcome = await Promise.race([
            iterator.return!().then(() => 'settled' as const),
            delay(2000).then(() => 'hung' as const),
        ]);

        // assert
        expect(outcome).toBe('settled');
    });

    it('still ends normally when upstream completes', async () => {
        // arrange
        const source = (async function* (): AsyncIterable<ArrivedChunk> {
            await delay(0);
            yield makeChunk();
        })();
        const iterator = drainOf(source);

        // act
        const first = await iterator.next();
        const second = await iterator.next();

        // assert
        expect(first.done).toBe(false);
        expect(second.done).toBe(true);
    });

    it('stops pulling upstream once the consumer is gone', async () => {
        // arrange
        let produced = 0;
        const source = (async function* (): AsyncIterable<ArrivedChunk> {
            for (;;) {
                produced++;
                yield makeChunk();

                await delay(0);
            }
        })();
        const iterator = drainOf(source);
        await iterator.next();

        // act
        await iterator.return!();
        const producedAtStop = produced;
        await delay(50);

        // assert
        expect(produced).toBe(producedAtStop);
    });
});

function drainOf(source: AsyncIterable<ArrivedChunk>): AsyncIterator<ArrivedChunk> {
    const buffer = new EncodedFrameBuffer({ targetSpanMs: 0, nowFn: () => 0 });
    const out = pacedEncodedBuffer({ buffer })(source as never) as AsyncIterable<ArrivedChunk>;
    return out[Symbol.asyncIterator]();
}

function endlessChunks(): AsyncIterable<ArrivedChunk> {
    return (async function* (): AsyncIterable<ArrivedChunk> {
        for (;;) {
            yield makeChunk();

            await delay(0);
        }
    })();
}

function makeChunk(): ArrivedChunk {
    return {
        chunk: { type: 'key', timestamp: 0, duration: 33_000, byteLength: 1 } as unknown as EncodedVideoChunk,
        arrivedAt: { timeMs: 0, epoch: 0 },
        capturedAt: { timeMs: 0, epoch: 0 },
        index: 0,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame: true,
    } as unknown as ArrivedChunk;
}

function delay(ms: number): Promise<void> {
    return new Promise<void>(resolve => setTimeout(resolve, ms));
}
