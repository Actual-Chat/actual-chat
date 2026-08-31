import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    detectSupportedCodecs,
    excludeEncoderCodecString,
    isEncoderCodecStringExcluded,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';

// detectSupportedCodecs caches per `WxH`, and the exclusion set is module state
// that only grows, so every test picks dimensions no other test uses.
let nextWidth = 1000;
function freshSize(): { width: number; height: number } {
    nextWidth += 2;

    return { width: nextWidth, height: 720 };
}

function h264Of(codecs: Awaited<ReturnType<typeof detectSupportedCodecs>>) {
    return codecs.find(c => c.category === 'h264')!;
}

type EncoderProbe = (config: VideoEncoderConfig) => Promise<VideoEncoderSupport>;

describe('encoder codec detection', () => {
    let isConfigSupported: ReturnType<typeof vi.fn<EncoderProbe>>;
    let probed: string[];

    beforeEach(() => {
        probed = [];
        isConfigSupported = vi.fn<EncoderProbe>(config => {
            probed.push(config.codec);

            return Promise.resolve({ supported: false, config });
        });
        vi.stubGlobal('VideoEncoder', { isConfigSupported });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    const supportOnly = (...codecs: string[]): EncoderProbe =>
        config => {
            probed.push(config.codec);

            return Promise.resolve({ supported: codecs.includes(config.codec), config });
        };

    it('reports the profile it probed, not a higher one it did not', async () => {
        // The old code probed Main 3.1 and then reported High 4.0 regardless.
        isConfigSupported.mockImplementation(supportOnly('avc1.42E01F'));
        const size = freshSize();

        const h264 = h264Of(await detectSupportedCodecs(size.width, size.height));

        expect(h264.supported).toBe(true);
        expect(h264.codec).toBe('avc1.42E01F');
        expect(probed).toContain('avc1.42E01F');
    });

    it('never probes Main or High, which carry CABAC and B-frames', async () => {
        isConfigSupported.mockImplementation(supportOnly('avc1.640028', 'avc1.4D401F', 'avc1.42E01F'));
        const size = freshSize();

        const h264 = h264Of(await detectSupportedCodecs(size.width, size.height));

        expect(h264.codec).toBe('avc1.42E01F');
        expect(probed).not.toContain('avc1.640028');
        expect(probed).not.toContain('avc1.4D401F');
    });

    it('falls all the way to Constrained Baseline', async () => {
        isConfigSupported.mockImplementation(supportOnly('avc1.42E01F'));
        const size = freshSize();

        const h264 = h264Of(await detectSupportedCodecs(size.width, size.height));

        expect(h264.codec).toBe('avc1.42E01F');
    });

    it('marks H.264 unsupported when no profile passes', async () => {
        const size = freshSize();

        const h264 = h264Of(await detectSupportedCodecs(size.width, size.height));

        expect(h264.supported).toBe(false);
        expect(h264.hardwareAccelerated).toBe(false);
    });

    // Last in this describe: the exclusion set is module state that only grows,
    // and H.264 now has exactly one profile string to exclude.
    it('drops H.264 once its one profile is excluded, re-probing the same size', async () => {
        // The bug this guards: excludeEncoderCodec refuses to drop the h264
        // category, so a failed profile was re-picked on every restart forever.
        // Re-detecting at the SAME size also proves the detection cache was
        // invalidated — a stale entry would still report the codec supported.
        isConfigSupported.mockImplementation(supportOnly('avc1.42E01F'));
        const size = freshSize();
        const before = h264Of(await detectSupportedCodecs(size.width, size.height));
        expect(before.supported).toBe(true);

        excludeEncoderCodecString('avc1.42E01F');
        expect(isEncoderCodecStringExcluded('avc1.42E01F')).toBe(true);

        probed = [];
        const after = h264Of(await detectSupportedCodecs(size.width, size.height));

        expect(after.supported).toBe(false);
        expect(probed).not.toContain('avc1.42E01F');
    });
});
