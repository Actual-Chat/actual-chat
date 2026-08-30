import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    getCodecCandidates,
    selectDecoderCodec,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/hevc-codec-selection';

// avc1.PPCCLL — 42 Constrained Baseline < 4D Main < 64 High.
function profileOf(codec: string): number {
    return Number.parseInt(codec.slice(5, 7), 16);
}

function levelOf(codec: string): number {
    return Number.parseInt(codec.slice(9, 11), 16);
}

describe('getCodecCandidates: H.264', () => {
    it('offers the declared string first', () => {
        expect(getCodecCandidates('avc1.640028')[0]).toBe('avc1.640028');
    });

    it('never offers a lower profile or level than the declared one', () => {
        // Declaring less than the bitstream carries makes configure() succeed and
        // decode() drop chunks silently, so the ladder may only widen.
        for (const declared of ['avc1.42E01F', 'avc1.4D4029', 'avc1.640028']) {
            for (const candidate of getCodecCandidates(declared)) {
                expect(profileOf(candidate)).toBeGreaterThanOrEqual(profileOf(declared));
                expect(levelOf(candidate)).toBeGreaterThanOrEqual(levelOf(declared));
            }
        }
    });

    it('widens to the same profile at L5.2 and then to High', () => {
        expect(getCodecCandidates('avc1.4D4029')).toEqual([
            'avc1.4D4029',
            'avc1.4D4034',
            'avc1.640034',
        ]);
    });

    it('does not repeat a candidate that is already the declared string', () => {
        const candidates = getCodecCandidates('avc1.640034');
        expect(new Set(candidates).size).toBe(candidates.length);
    });

    it('leaves non-AVC codec strings alone', () => {
        expect(getCodecCandidates('vp09.00.31.08')).toEqual(['vp09.00.31.08']);
    });

    it('maps an unnamed H.264 stream to Constrained Baseline', () => {
        expect(getCodecCandidates('h264')[0]).toBe('avc1.42E01F');
    });
});

type DecoderProbe = (config: VideoDecoderConfig) => Promise<{ supported: boolean }>;

describe('selectDecoderCodec', () => {
    let isConfigSupported: ReturnType<typeof vi.fn<DecoderProbe>>;

    beforeEach(() => {
        isConfigSupported = vi.fn<DecoderProbe>();
        vi.stubGlobal('VideoDecoder', { isConfigSupported });
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('picks the first hardware-supported candidate', async () => {
        isConfigSupported.mockImplementation(c => Promise.resolve({ supported: c.codec === 'b' }));

        const selection = await selectDecoderCodec(['a', 'b', 'c'], undefined);

        expect(selection).toEqual({ codec: 'b', hardwareAcceleration: 'prefer-hardware' });
    });

    it('retries every candidate with no-preference before giving up', async () => {
        // Firefox rejects prefer-hardware for codecs it decodes in software.
        isConfigSupported.mockImplementation(c =>
            Promise.resolve({ supported: c.hardwareAcceleration === 'no-preference' }));

        const selection = await selectDecoderCodec(['a', 'b'], undefined);

        expect(selection).toEqual({ codec: 'a', hardwareAcceleration: 'no-preference' });
        expect(isConfigSupported.mock.calls.map(([c]) => c.hardwareAcceleration))
            .toEqual(['prefer-hardware', 'prefer-hardware', 'no-preference']);
    });

    it('prefers hardware over a software match on an earlier candidate', async () => {
        isConfigSupported.mockImplementation(c =>
            Promise.resolve({ supported: c.codec === 'b' || c.hardwareAcceleration === 'no-preference' }));

        const selection = await selectDecoderCodec(['a', 'b'], undefined);

        expect(selection).toEqual({ codec: 'b', hardwareAcceleration: 'prefer-hardware' });
    });

    it('skips excluded candidates in both passes', async () => {
        isConfigSupported.mockResolvedValue({ supported: true });

        const selection = await selectDecoderCodec(['a', 'b'], undefined, undefined, new Set(['a']));

        expect(selection).toEqual({ codec: 'b', hardwareAcceleration: 'prefer-hardware' });
    });

    it('returns null when nothing is supported either way', async () => {
        isConfigSupported.mockResolvedValue({ supported: false });

        expect(await selectDecoderCodec(['a', 'b'], undefined)).toBeNull();
    });

    it('treats a throwing probe as unsupported', async () => {
        isConfigSupported.mockImplementation(c => {
            if (c.codec === 'a')
                throw new Error('bad codec string');

            return Promise.resolve({ supported: true });
        });

        const selection = await selectDecoderCodec(['a', 'b'], undefined);

        expect(selection).toEqual({ codec: 'b', hardwareAcceleration: 'prefer-hardware' });
    });
});
