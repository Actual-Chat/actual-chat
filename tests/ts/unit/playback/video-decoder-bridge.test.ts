import { describe, it, expect } from 'vitest';
import {
    VideoDecoderBridge,
    type DecoderLike,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/playback/video-decoder-bridge';
import {
    createEmptyPlayerStats,
    type ArrivedChunk,
    type PlayerStats,
} from '../../../../src/dotnet/UI.Blazor.App/Services/Video/frame-envelopes';

// Pins the hang-watchdog and deficit-input accounting: one hang per stall
// (the in-flight FIFO is dropped so the watchdog disarms instead of re-firing
// every 2s through the keyframe wait), and chunksReceived counts only chunks
// actually submitted to the decoder (keyframe-wait drops are not decoder input).

class MockDecoder implements DecoderLike {
    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    decodeQueueSize = 0;
    decodeCalls = 0;
    constructor(
        public handlers: { onFrame: (f: VideoFrame) => void; onError: (e: Error) => void },
    ) { /* nothing */ }
    configure(_config: VideoDecoderConfig): void {
        this.state = 'configured';
    }
    decode(_chunk: EncodedVideoChunk): void {
        this.decodeCalls++;
    }
    flush(): Promise<void> {
        return Promise.resolve();
    }
    close(): void {
        this.state = 'closed';
    }
}

class MockEncodedVideoChunk {
    closed = false;
    timestamp = 0;
    duration: number | null = null;
    constructor(public type: 'key' | 'delta', public byteLength = 16) {}
    close(): void {
        this.closed = true;
    }
}

function makeArrived(stats: PlayerStats, isKeyFrame: boolean, index = 0): ArrivedChunk {
    return {
        chunk: new MockEncodedVideoChunk(isKeyFrame ? 'key' : 'delta') as unknown as EncodedVideoChunk,
        arrivedAt: { timeMs: 1_000 + index * 33, epoch: 0 },
        capturedAt: { timeMs: index * 33, epoch: 0 },
        index,
        dropTrace: [],
        serverArrivedAtUnixMs: 0,
        isKeyFrame,
        layerId: 0,
        width: 1280,
        height: 720,
        rawByteLength: 16,
        rotation: 0,
        stats,
    };
}

function makeBridge(): { bridge: VideoDecoderBridge; decoders: MockDecoder[]; clock: { now: number } } {
    const decoders: MockDecoder[] = [];
    const clock = { now: 0 };
    const bridge = new VideoDecoderBridge({
        initialConfig: { codec: 'avc1.640028' },
        createDecoder: handlers => {
            const d = new MockDecoder(handlers);
            decoders.push(d);
            return d;
        },
        framesUntilProven: 3,
        maxRecoveries: 5,
        decoderHangTimeoutMs: 2000,
        frameDurationMs: 33,
        now: () => clock.now,
    });
    return { bridge, decoders, clock };
}

describe('VideoDecoderBridge', () => {
    it('one hang per stall: watchdog drops the FIFO and disarms instead of re-firing', () => {
        // arrange: a submitted keyframe that never produces output
        const { bridge, clock } = makeBridge();
        const stats = createEmptyPlayerStats();
        bridge.submit(makeArrived(stats, true, 0));
        expect(bridge.inFlight).toBe(1);
        expect(bridge.hangDeadline()).toBe(2000);

        // act
        clock.now = 2000;
        bridge.onWatchdogFired(clock.now);

        // assert: counted once, FIFO dropped, watchdog disarmed
        expect(stats.hangRateIn60s).toBe(1);
        expect(bridge.inFlight).toBe(0);
        expect(stats.decoderQueueSize).toBe(0);
        expect(bridge.hangDeadline()).toBeNull();
    });

    it('keyframe-wait drops are not counted as decoder input', () => {
        // arrange: stalled decoder (pendingError set by the watchdog)
        const { bridge, clock } = makeBridge();
        const stats = createEmptyPlayerStats();
        bridge.submit(makeArrived(stats, true, 0));
        const submittedBefore = stats.chunksReceived;
        clock.now = 2000;
        bridge.onWatchdogFired(clock.now);

        // act: deltas arriving during the keyframe wait are dropped
        for (let i = 1; i <= 3; i++)
            bridge.submit(makeArrived(stats, false, i));

        // assert: no deficit input from undecodable drops, watchdog stays disarmed
        expect(stats.chunksReceived).toBe(submittedBefore);
        expect(bridge.hangDeadline()).toBeNull();
    });

    it('recovery keyframe recreates the decoder and resumes input accounting', () => {
        // arrange: stall, then wait for the recovery keyframe
        const { bridge, decoders, clock } = makeBridge();
        const stats = createEmptyPlayerStats();
        bridge.submit(makeArrived(stats, true, 0));
        clock.now = 2000;
        bridge.onWatchdogFired(clock.now);

        // act
        bridge.submit(makeArrived(stats, true, 1));

        // assert: fresh decoder, chunk submitted and counted, queue depth fresh
        expect(decoders).toHaveLength(2);
        expect(decoders[1].decodeCalls).toBe(1);
        expect(stats.chunksReceived).toBe(2);
        expect(stats.decoderQueueSize).toBe(1);
        expect(bridge.hangDeadline()).toBe(4000);
    });

    it('stamps submit/decode liveness and tracks decodedReadyCount', () => {
        // arrange: submit one keyframe, then fire onFrame via the fake decoder
        const { bridge, decoders } = makeBridge();
        const stats = createEmptyPlayerStats();
        bridge.submit(makeArrived(stats, true, 0));

        // act
        decoders[0].handlers.onFrame({ close: () => { /* nothing */ } } as unknown as VideoFrame);

        // assert
        expect(stats.lastSubmitAtMs).toBeGreaterThan(0);
        expect(stats.lastDecodeOutAtMs).toBeGreaterThan(0);
        expect(stats.decodedReadyCount).toBe(1);
        bridge.tryPull();
        expect(stats.decodedReadyCount).toBe(0);
    });
});
