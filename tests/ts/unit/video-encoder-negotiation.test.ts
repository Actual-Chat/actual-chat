import { describe, expect, it, vi } from 'vitest';
import {
    selectEncoderCandidates,
    type CodecInfo,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';

vi.mock('device-info', () => ({ DeviceInfo: { isFirefox: false } }));

const codecs: CodecInfo[] = [
    {
        name: 'VP9', codec: 'vp09.00.31.08', category: 'vp9', supported: true,
        hardwareSupported: false, softwareSupported: true, hardwareAccelerated: false, realtime: true,
    },
    {
        name: 'HEVC', codec: 'hvc1.1.6.L93.90', category: 'hevc', supported: true,
        hardwareSupported: true, softwareSupported: false, hardwareAccelerated: true, realtime: true,
    },
];

describe('negotiated encoder selection', () => {
    it('should prefer hardware HEVC over software VP9 when the audience permits it', () => {
        const candidates = selectEncoderCandidates(codecs, new Set(['hevc', 'vp9']), null);

        expect(candidates[0].info.category).toBe('hevc');
        expect(candidates[0].accel).toBe('prefer-hardware');
    });

    it('should retain software VP9 when a viewer cannot decode HEVC', () => {
        const candidates = selectEncoderCandidates(codecs, new Set(['vp9']), null);

        expect(candidates).toHaveLength(1);
        expect(candidates[0].info.category).toBe('vp9');
        expect(candidates[0].accel).toBe('prefer-software');
    });

    it('should reject a hardware encoder that failed the realtime probe', () => {
        const candidates = selectEncoderCandidates(
            codecs.map(c => c.category === 'hevc' ? { ...c, realtime: false } : c),
            new Set(['hevc', 'vp9']),
            null,
        );

        expect(candidates).toHaveLength(1);
        expect(candidates[0].info.category).toBe('vp9');
    });
});
