import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    detectSupportedDecoderCodecs,
    excludeDecoderCodec,
    excludeEncoderCodec,
    excludeEncoderCodecString,
    FLOOR_CATEGORY,
    getExcludedDecoderCodecs,
    getExcludedEncoderCodecs,
    isEncoderCodecStringExcluded,
    probeEncoderLatencyFrames,
    setForceH264Only,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';

type DecoderProbe = (config: VideoDecoderConfig) => Promise<{ supported: boolean }>;

describe('decoder capability detection', () => {
    let isConfigSupported: ReturnType<typeof vi.fn<DecoderProbe>>;

    beforeEach(() => {
        isConfigSupported = vi.fn<DecoderProbe>();
        vi.stubGlobal('VideoDecoder', { isConfigSupported });
        // detectSupportedDecoderCodecs memoises; this is the public reset.
        setForceH264Only(false);
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

    // Emits its first chunk only after `latencyFrames` submissions, which is
    // how Firefox's H.264 encoder behaves.
    class FakeEncoder {
        state: CodecState = 'unconfigured';
        submitted = 0;
        private output: (chunk: EncodedVideoChunk) => void;
        constructor(init: { output: (chunk: EncodedVideoChunk) => void; error: (e: Error) => void }) {
            this.output = init.output;
            encoders.push(this);
        }
        configure(): void { this.state = 'configured'; }
        encode(): void {
            this.submitted++;
            if (this.submitted >= FakeEncoder.latencyFrames)
                this.output({ byteLength: 10, close: () => { /* ignore */ } } as unknown as EncodedVideoChunk);
        }
        close(): void { this.state = 'closed'; }
        static latencyFrames = 1;
    }

    beforeEach(() => {
        encoders = [];
        FakeEncoder.latencyFrames = 1;
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

    it('reports the submission count at which the first chunk appeared', async () => {
        FakeEncoder.latencyFrames = 1;

        expect(await probeEncoderLatencyFrames('vp09.00.31.08', 'vp9', 'no-preference')).toBe(1);
    });

    it('reports a deep pipeline like the one that makes Firefox H.264 unusable', async () => {
        FakeEncoder.latencyFrames = 18;

        expect(await probeEncoderLatencyFrames('avc1.4D4029', 'h264', 'no-preference')).toBe(18);
    });

    it('caches per codec and acceleration', async () => {
        FakeEncoder.latencyFrames = 2;
        await probeEncoderLatencyFrames('vp8', 'vp9', 'no-preference');
        const afterFirst = encoders.length;
        await probeEncoderLatencyFrames('vp8', 'vp9', 'no-preference');

        expect(encoders.length).toBe(afterFirst);
    });
});
