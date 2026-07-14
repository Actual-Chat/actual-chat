import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RpcStreamSender } from '../../../src/nodejs/src/actuallab-rpc/rpc-stream-sender';
import type { RpcPeer } from '../../../src/nodejs/src/actuallab-rpc/rpc-peer';
import { computeUplinkAckAdvance, expandVideo, type VideoConstants } from '../../../src/nodejs/src/app-constants';

// Publisher-leg simulation: paced producer vs. a consumer whose ACKs arrive one
// full RTT after each item. Bundle-count flow control caps throughput at
// ackAdvance / RTT regardless of bandwidth, so a window below the BDP
// (fps × RTT) starves the stream and forces canSkipTo compaction — the root
// cause of the cross-continent L0 collapse. Pins the mechanism and the config.

interface TestFrame {
    index: number;
    isKeyFrame: boolean;
}

interface SimOptions {
    ackPeriod: number;
    ackAdvance: number;
    bufferSize: number;
    rttMs: number;
    frameDurationMs: number;
    frameCount: number;
    keyFramePeriod: number;
}

interface SimResult {
    delivered: TestFrame[];
    skipCount: number;
}

async function runPublishSim(options: SimOptions): Promise<SimResult> {
    const delivered: TestFrame[] = [];
    let receivedCount = 0;
    let sender: RpcStreamSender<TestFrame> = undefined!;
    const onItem = (item: TestFrame): void => {
        delivered.push(item);
        receivedCount++;
        const ackIndex = receivedCount;
        if (ackIndex % options.ackPeriod === 0)
            setTimeout(() => sender.onAck(ackIndex, ''), options.rttMs);
    };
    const peerStub = {
        hub: {
            hubId: 'test-host',
            systemCallSender: {
                item: (
                    _conn: unknown, _format: unknown, _localId: number,
                    _index: number, item: TestFrame,
                ) => onItem(item),
                batch: (
                    _conn: unknown, _format: unknown, _localId: number,
                    _index: number, items: TestFrame[],
                ) => items.forEach(onItem),
                end: () => undefined,
            },
        },
        sharedObjects: {
            nextId: () => 1,
            unregister: () => undefined,
        },
        connection: {},
        serializationFormat: null,
    };
    sender = new RpcStreamSender<TestFrame>(
        peerStub as unknown as RpcPeer,
        options.ackPeriod,
        options.ackAdvance,
        true,
        true,
        f => f.isKeyFrame,
        false,
        options.bufferSize,
    );
    // Push-driven source mirroring push-to-pull-buffer: capture pushes frames
    // on a wall-clock cadence regardless of wire pull, so backlog lands in the
    // sender ring instead of pacing the producer down to the wire rate.
    const queue: TestFrame[] = [];
    let queueWaiter: (() => void) | null = null;
    let producedCount = 0;
    const pushFrame = (): void => {
        queue.push({ index: producedCount, isKeyFrame: producedCount % options.keyFramePeriod === 0 });
        producedCount++;
        queueWaiter?.();
        queueWaiter = null;
        if (producedCount < options.frameCount)
            setTimeout(pushFrame, options.frameDurationMs);
    };
    setTimeout(pushFrame, 0);
    const source = (async function* (): AsyncGenerator<TestFrame> {
        let consumedCount = 0;
        while (consumedCount < options.frameCount) {
            if (queue.length === 0)
                await new Promise<void>(resolve => { queueWaiter = resolve; });
            while (queue.length > 0) {
                yield queue.shift()!;

                consumedCount++;
            }
        }
    })();
    const pump = sender.writeFrom(source);
    sender.onAck(0, '');
    const totalMs = options.frameCount * options.frameDurationMs + 40 * options.rttMs + 5000;
    await vi.advanceTimersByTimeAsync(totalMs);
    sender.disconnect();
    await pump;
    return { delivered, skipCount: sender.skipCount };
}

const baseVideo = {
    frameRate: 30,
    targetBufferSize: 6,
    keyFramePeriodMs: 3000,
    serverReplayTailDurationMs: 3300,
} as VideoConstants;

describe('RpcStreamSender flow-control window', () => {
    beforeEach(() => vi.useFakeTimers());
    afterEach(() => vi.useRealTimers());

    it('caps throughput at ackAdvance/RTT and skips frames when the window is below BDP', async () => {
        // act
        // 6 credits / 0.5s RTT = 12 items/s vs ~30 fps produced: the ring
        // saturates and canSkipTo compaction discards most of each GOP.
        const result = await runPublishSim({
            ackPeriod: 2,
            ackAdvance: 6,
            bufferSize: 120,
            rttMs: 500,
            frameDurationMs: 33,
            frameCount: 600,
            keyFramePeriod: 90,
        });

        // assert
        expect(result.skipCount).toBeGreaterThan(0);
        expect(result.delivered.length).toBeLessThan(450);
    });

    it('shipped videoRecording window sustains full frame rate at 500ms RTT', async () => {
        // arrange
        const video = expandVideo(baseVideo);

        // act
        const result = await runPublishSim({
            ackPeriod: video.rpcStreamAckPeriod,
            ackAdvance: video.rpcStreamAckAdvance,
            bufferSize: video.senderBufferSize,
            rttMs: 500,
            frameDurationMs: 33,
            frameCount: 600,
            keyFramePeriod: video.keyFramePeriodSize,
        });

        // assert
        expect(result.skipCount).toBe(0);
        expect(result.delivered.length).toBe(600);
        expect(result.delivered.at(-1)?.index).toBe(599);
    });

    it('computeUplinkAckAdvance scales the window with measured min RTT', () => {
        // act + assert: unmeasured and short-RTT links keep the static floor;
        // long-RTT links grow to cover BDP × 2 + slack, capped at the ring size.
        expect(computeUplinkAckAdvance(30, -1, 45, 120)).toBe(45);
        expect(computeUplinkAckAdvance(30, 200, 45, 120)).toBe(45);
        expect(computeUplinkAckAdvance(30, 1000, 45, 120)).toBe(66);
        expect(computeUplinkAckAdvance(30, 3000, 45, 120)).toBe(120);
    });
});
