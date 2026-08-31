import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    detectSupportedDecoderCodecs,
    excludeDecoderCodec,
    excludeEncoderCodec,
    excludeEncoderCodecString,
    FLOOR_CATEGORY,
    FORCED_CODEC_MARKER,
    getExcludedDecoderCodecs,
    getExcludedEncoderCodecs,
    isEncoderCodecStringExcluded,
    MAX_REALTIME_LATENCY_FRAMES,
    probeEncoderLatencyFrames,
    setForceDecodeCodec,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';

type DecoderProbe = (config: VideoDecoderConfig) => Promise<{ supported: boolean }>;

describe('decoder capability detection', () => {
    let isConfigSupported: ReturnType<typeof vi.fn<DecoderProbe>>;

    beforeEach(() => {
        isConfigSupported = vi.fn<DecoderProbe>();
        vi.stubGlobal('VideoDecoder', { isConfigSupported });
        // node has no localStorage, so without this the debug overrides read
        // back as null and the force path is never exercised.
        const store = new Map<string, string>();
        vi.stubGlobal('localStorage', {
            getItem: (k: string) => store.get(k) ?? null,
            setItem: (k: string, v: string) => { store.set(k, v); },
            removeItem: (k: string) => { store.delete(k); },
        });
        // detectSupportedDecoderCodecs memoises; setting the override clears it.
        setForceDecodeCodec(null);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('probes H.264 at the bottom of the profile ladder rather than asserting it', async () => {
        // The old code hard-coded ['h264'] and never asked. The floor of the
        // ladder is what answers "is there an H.264 decoder at all".
        isConfigSupported.mockResolvedValue({ supported: true });

        await detectSupportedDecoderCodecs();

        const h264Probes = isConfigSupported.mock.calls
            .map(([c]) => c.codec)
            .filter(c => c.startsWith('avc1.'));
        expect(h264Probes.length).toBeGreaterThan(0);
        expect(h264Probes[0]).toBe('avc1.42E01E');
    });

    it('advertises the forced codec behind the marker, without the floor', async () => {
        // A pin, not a capability report: the floor must not come back on
        // its own, or the call would keep a codec the admin excluded.
        isConfigSupported.mockResolvedValue({ supported: true });
        setForceDecodeCodec('h264');

        const codecs = await detectSupportedDecoderCodecs();

        expect(codecs).toEqual([FORCED_CODEC_MARKER, 'h264']);
        expect(codecs).not.toContain(FLOOR_CATEGORY);
        setForceDecodeCodec(null);
    });

    it('advertises every decodable codec, as a set', async () => {
        // Order carries no meaning: the server treats the list as a set, and
        // which codec a sender uses comes from its own encoder ladder.
        isConfigSupported.mockResolvedValue({ supported: true });

        const codecs = await detectSupportedDecoderCodecs();

        expect([...codecs].sort()).toEqual(['av1', 'h264', 'hevc', 'vp9']);
    });

    it('marks a forced floor as a pin too, so it is not intersected away', async () => {
        isConfigSupported.mockResolvedValue({ supported: true });
        setForceDecodeCodec(FLOOR_CATEGORY);

        expect(await detectSupportedDecoderCodecs()).toEqual([FORCED_CODEC_MARKER, FLOOR_CATEGORY]);
        setForceDecodeCodec(null);
    });

    it('still advertises the floor when probing says it is unsupported', async () => {
        // A client that cannot decode the floor needs a decoder, not a
        // renegotiation — so it must never drop out of the advertised set.
        isConfigSupported.mockResolvedValue({ supported: false });

        expect(await detectSupportedDecoderCodecs()).toContain(FLOOR_CATEGORY);
    });
});

describe('the negotiation floor', () => {
    it('is VP9, not H.264', () => {
        expect(FLOOR_CATEGORY).toBe('vp9');
    });

    it('cannot be excluded from encoding, by category or by codec string', () => {
        excludeEncoderCodec(FLOOR_CATEGORY);
        excludeEncoderCodecString('vp09.00.31.08');

        expect(getExcludedEncoderCodecs()).not.toContain(FLOOR_CATEGORY);
        expect(isEncoderCodecStringExcluded('vp09.00.31.08')).toBe(false);
    });

    it('cannot be excluded from decoding', () => {
        excludeDecoderCodec(FLOOR_CATEGORY);

        expect(getExcludedDecoderCodecs()).not.toContain(FLOOR_CATEGORY);
    });

    it('lets H.264 be excluded, which the old universal-fallback guard refused', () => {
        excludeEncoderCodec('h264');
        excludeDecoderCodec('h264');

        expect(getExcludedEncoderCodecs()).toContain('h264');
        expect(getExcludedDecoderCodecs()).toContain('h264');
    });
});

type EncoderProbe = (config: VideoEncoderConfig) => Promise<VideoEncoderSupport>;

describe('encoder realtime measurement', () => {
    let encoders: FakeEncoder[];

    // Two independent knobs, because real encoders differ along both:
    // `startupFrames` is the silent initialisation window, `steadyDepth` is how
    // many frames stay in flight forever after it. Chromium's hardware encoders
    // are silent for ~7 frames and then catch up completely (startup 7, depth
    // 0); Firefox's H.264 stays ~18 frames behind for the whole stream.
    class FakeEncoder {
        state: CodecState = 'unconfigured';
        submitted = 0;
        emitted = 0;
        private output: (chunk: EncodedVideoChunk) => void;
        constructor(init: { output: (chunk: EncodedVideoChunk) => void; error: (e: Error) => void }) {
            this.output = init.output;
            encoders.push(this);
        }
        configure(): void { this.state = 'configured'; }
        encode(): void {
            this.submitted++;
            if (this.submitted < FakeEncoder.startupFrames)
                return;

            const target = Math.max(0, this.submitted - FakeEncoder.steadyDepth);
            // `drainPerTick` caps how much of the startup backlog comes out at
            // once. Unlimited (the default) matches Chromium, which hands over
            // the whole backlog in one callback burst; a real encoder may take
            // several ticks to catch up, and those ticks must not be read as
            // the encoder's steady-state depth.
            let budget = FakeEncoder.drainPerTick;
            while (this.emitted < target && budget-- > 0) {
                this.emitted++;
                this.output({ byteLength: 10, close: () => { /* ignore */ } } as unknown as EncodedVideoChunk);
            }
        }
        close(): void { this.state = 'closed'; }
        static startupFrames = 1;
        static steadyDepth = 0;
        static drainPerTick = Number.MAX_SAFE_INTEGER;
    }

    beforeEach(() => {
        encoders = [];
        FakeEncoder.startupFrames = 1;
        FakeEncoder.steadyDepth = 0;
        FakeEncoder.drainPerTick = Number.MAX_SAFE_INTEGER;
        vi.stubGlobal('VideoEncoder', Object.assign(FakeEncoder, {
            isConfigSupported: vi.fn<EncoderProbe>(),
        }));
        vi.stubGlobal('OffscreenCanvas', class {
            constructor(public width: number, public height: number) { /* stub */ }
            getContext(): unknown { return { fillStyle: '', fillRect: () => { /* stub */ } }; }
        });
        vi.stubGlobal('VideoFrame', class {
            close(): void { /* stub */ }
        });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('reports no depth for an encoder that keeps up', async () => {
        FakeEncoder.startupFrames = 1;

        expect(await probeEncoderLatencyFrames('vp09.00.31.08', 'vp9', 'no-preference')).toBe(0);
    });

    it('ignores a slow start that the encoder then catches up from', async () => {
        // Chromium's hardware encoders: ~215ms of silence, then every
        // submission accounted for. Charging that startup to the codec
        // disqualified AV1, H.264 and HEVC on machines where all three work.
        FakeEncoder.startupFrames = 7;
        FakeEncoder.steadyDepth = 0;

        expect(await probeEncoderLatencyFrames('av01.0.08M.08', 'av1', 'prefer-hardware')).toBe(0);
    });

    it('does not charge a codec for the tick its startup backlog drains on', async () => {
        // The backlog comes out over several ticks rather than all at once, so
        // depth reads high once and then settles. Reporting that transient max
        // disqualified an encoder that keeps up perfectly afterwards.
        FakeEncoder.startupFrames = 7;
        FakeEncoder.steadyDepth = 0;
        FakeEncoder.drainPerTick = 2;

        // A codec string no other test probes: latencyProbeCache is module
        // state with no invalidation, so a shared key returns a stale verdict.
        // The depth reported inside the qualifying range is informational —
        // what matters is that the codec still qualifies.
        const frames = await probeEncoderLatencyFrames('av01.0.09M.08', 'av1', 'prefer-hardware');

        expect(frames).toBeLessThanOrEqual(MAX_REALTIME_LATENCY_FRAMES);
    });

    it('reports a deep pipeline like the one that makes Firefox H.264 unusable', async () => {
        FakeEncoder.startupFrames = 18;
        FakeEncoder.steadyDepth = 17;

        expect(await probeEncoderLatencyFrames('avc1.4D4029', 'h264', 'no-preference')).toBe(17);
    });

    it('caches per codec and acceleration', async () => {
        FakeEncoder.startupFrames = 2;
        await probeEncoderLatencyFrames('vp8', 'vp9', 'no-preference');
        const afterFirst = encoders.length;
        await probeEncoderLatencyFrames('vp8', 'vp9', 'no-preference');

        expect(encoders.length).toBe(afterFirst);
    });
});
