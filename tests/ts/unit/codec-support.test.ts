import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { VideoCodecKind } from '../../../src/nodejs/src/app-constants';

// Pins the Firefox-lie handling (bugzil.la/1918769): isConfigSupported=true
// with a failing configure() must fail the probe; h264 is runtime-excludable
// unless proven; SW VP9 is eligible only as the last resort.

const deviceInfoMock = vi.hoisted(() => ({
    isMobile: false,
    isFirefox: false,
    isIos: false,
    isSafari: false,
    isAndroid: false,
}));
vi.mock('device-info', () => ({ DeviceInfo: deviceInfoMock }));

const TEST_VIDEO = {
    codecDefs: [
        { kind: VideoCodecKind.Unknown, efficiency: 1 },
        { kind: VideoCodecKind.H264, efficiency: 1 },
        { kind: VideoCodecKind.Hevc, efficiency: 2 },
        { kind: VideoCodecKind.Vp9, efficiency: 2.35 },
        { kind: VideoCodecKind.Av1, efficiency: 2.85 },
    ],
};
vi.mock('app-constants', async importOriginal => {
    const mod = await importOriginal<typeof import('app-constants')>();
    return {
        ...mod,
        getVideoCodecEfficiency: (codec: string) => mod.getVideoCodecEfficiency(codec, TEST_VIDEO),
    };
});

type ConfigureBehavior = 'ok' | 'throw' | 'async-error' | 'hang-flush';

interface MockVideoEncoderInit {
    output: (chunk: unknown, metadata: unknown) => void;
    error: (e: unknown) => void;
}

class MockVideoEncoder {
    static instances: MockVideoEncoder[] = [];
    static isConfigSupportedResult = true;
    static configureBehavior: ConfigureBehavior = 'ok';

    static reset(): void {
        MockVideoEncoder.instances = [];
        MockVideoEncoder.isConfigSupportedResult = true;
        MockVideoEncoder.configureBehavior = 'ok';
    }

    static isConfigSupported(config: VideoEncoderConfig): Promise<{ supported: boolean; config: VideoEncoderConfig }> {
        return Promise.resolve({ supported: MockVideoEncoder.isConfigSupportedResult, config });
    }

    state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
    error: MockVideoEncoderInit['error'];

    constructor(init: MockVideoEncoderInit) {
        this.error = init.error;
        MockVideoEncoder.instances.push(this);
    }

    configure(_config: VideoEncoderConfig): void {
        const behavior = MockVideoEncoder.configureBehavior;
        if (behavior === 'throw')
            throw new DOMException('Operation is not supported', 'NotSupportedError');

        if (behavior === 'async-error')
            queueMicrotask(() => this.error(
                new DOMException('The given encoding is not supported', 'NotSupportedError')));

        this.state = 'configured';
    }

    flush(): Promise<void> {
        return MockVideoEncoder.configureBehavior === 'hang-flush'
            ? new Promise<void>(() => undefined)
            : Promise.resolve();
    }

    close(): void {
        this.state = 'closed';
    }
}

type CodecSupport = typeof import('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support');

async function importCodecSupport(): Promise<CodecSupport> {
    return await import('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support');
}

const PROBE_LAYERS = [{ width: 1280, height: 720, bitrateKbps: 2_500 }];

function codecInfo(
    category: 'h264' | 'hevc' | 'av1' | 'vp9',
    codec: string,
    hardwareAccelerated: boolean,
    supported = true,
): import('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support').CodecInfo {
    return { name: codec, codec, category, supported, hardwareAccelerated };
}

beforeEach(() => {
    vi.resetModules();
    MockVideoEncoder.reset();
    deviceInfoMock.isMobile = false;
    vi.stubGlobal('VideoEncoder', MockVideoEncoder);
});

afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
});

describe('probeEncoder', () => {
    it('should fail when isConfigSupported lies and configure throws (Firefox H.264)', async () => {
        // arrange
        const cs = await importCodecSupport();
        MockVideoEncoder.configureBehavior = 'throw';

        // act
        const result = await cs.probeEncoder('avc1.4D4029', PROBE_LAYERS);

        // assert
        expect(result.supported).toBe(false);
        expect(result.failedStage).toBe('configure');
        expect(MockVideoEncoder.instances).toHaveLength(1);
        expect(MockVideoEncoder.instances[0].state).toBe('closed');
    });

    it('should fail when the error callback fires before flush resolves', async () => {
        // arrange
        const cs = await importCodecSupport();
        MockVideoEncoder.configureBehavior = 'async-error';

        // act
        const result = await cs.probeEncoder('avc1.4D4029', PROBE_LAYERS);

        // assert
        expect(result.supported).toBe(false);
        expect(result.failedStage).toBe('configure');
        expect(MockVideoEncoder.instances[0].state).toBe('closed');
    });

    it('should pass when configure and flush succeed', async () => {
        // arrange
        const cs = await importCodecSupport();

        // act
        const result = await cs.probeEncoder('avc1.4D4029', PROBE_LAYERS);

        // assert
        expect(result.supported).toBe(true);
        expect(result.failedStage).toBeNull();
        expect(MockVideoEncoder.instances[0].state).toBe('closed');
    });

    it('should fail on isConfigSupported=false without creating an encoder', async () => {
        // arrange
        const cs = await importCodecSupport();
        MockVideoEncoder.isConfigSupportedResult = false;

        // act
        const result = await cs.probeEncoder('avc1.4D4029', PROBE_LAYERS);

        // assert
        expect(result.supported).toBe(false);
        expect(MockVideoEncoder.instances).toHaveLength(0);
    });

    it('should treat a flush timeout as supported (slow-but-live encoder)', async () => {
        // arrange
        vi.useFakeTimers();
        const cs = await importCodecSupport();
        MockVideoEncoder.configureBehavior = 'hang-flush';

        // act
        const whenProbed = cs.probeEncoder('avc1.4D4029', PROBE_LAYERS);
        await vi.advanceTimersByTimeAsync(3_100);
        const result = await whenProbed;

        // assert
        expect(result.supported).toBe(true);
        expect(MockVideoEncoder.instances[0].state).toBe('closed');
    });
});

describe('excludeEncoderCodec', () => {
    it('should exclude h264 when it is not proven', async () => {
        // arrange
        const cs = await importCodecSupport();

        // act
        cs.excludeEncoderCodec('h264');

        // assert
        expect(cs.isEncoderCodecExcluded('h264')).toBe(true);
    });

    it('should not exclude a codec proven this session', async () => {
        // arrange
        const cs = await importCodecSupport();
        cs.markEncoderCodecProven('h264');

        // act
        cs.excludeEncoderCodec('h264');

        // assert
        expect(cs.isEncoderCodecExcluded('h264')).toBe(false);
    });
});

describe('getDefaultCodec', () => {
    it('should return null when every category is excluded', async () => {
        // arrange
        const cs = await importCodecSupport();
        cs.excludeEncoderCodec('h264');

        // act
        const codec = cs.getDefaultCodec([codecInfo('h264', 'avc1.4D4029', false)], 1280, 720);

        // assert
        expect(codec).toBeNull();
    });

    it('should fall back to SW VP9 on desktop when h264 is excluded', async () => {
        // arrange
        const cs = await importCodecSupport();
        cs.excludeEncoderCodec('h264');
        const supported = [
            codecInfo('h264', 'avc1.4D4029', false),
            codecInfo('vp9', 'vp09.00.41.08', false),
        ];

        // act
        const codec = cs.getDefaultCodec(supported, 1280, 720);

        // assert
        expect(codec).toBe('vp09.00.41.08');
    });

    it('should not fall back to SW VP9 on mobile', async () => {
        // arrange
        deviceInfoMock.isMobile = true;
        const cs = await importCodecSupport();
        cs.excludeEncoderCodec('h264');
        const supported = [
            codecInfo('h264', 'avc1.4D4029', false),
            codecInfo('vp9', 'vp09.00.41.08', false),
        ];

        // act
        const codec = cs.getDefaultCodec(supported, 1280, 720);

        // assert
        expect(codec).toBeNull();
    });

    it('should keep the unconditional h264 fallback while h264 is not excluded', async () => {
        // arrange
        const cs = await importCodecSupport();

        // act
        const codec = cs.getDefaultCodec([], 1280, 720);

        // assert
        expect(codec).not.toBeNull();
        expect(cs.getCodecCategory(codec!)).toBe('h264');
    });
});

describe('listEncoderCandidatesByEfficiency', () => {
    it('should gate SW VP9 out while an h264 candidate exists', async () => {
        // arrange
        const cs = await importCodecSupport();
        const supported = [
            codecInfo('h264', 'avc1.4D4029', true),
            codecInfo('vp9', 'vp09.00.41.08', false),
        ];

        // act
        const candidates = cs.listEncoderCandidatesByEfficiency(supported, null);

        // assert
        expect(candidates.map(c => c.category)).toEqual(['h264']);
    });

    it('should admit SW VP9 as the last resort once h264 and hevc are gone', async () => {
        // arrange
        const cs = await importCodecSupport();
        cs.excludeEncoderCodec('h264');
        const supported = [
            codecInfo('h264', 'avc1.4D4029', true),
            codecInfo('vp9', 'vp09.00.41.08', false),
        ];

        // act
        const candidates = cs.listEncoderCandidatesByEfficiency(supported, null);

        // assert
        expect(candidates.map(c => c.category)).toEqual(['vp9']);
    });

    it('should never admit SW VP9 on mobile', async () => {
        // arrange
        deviceInfoMock.isMobile = true;
        const cs = await importCodecSupport();
        cs.excludeEncoderCodec('h264');

        // act
        const candidates = cs.listEncoderCandidatesByEfficiency(
            [codecInfo('vp9', 'vp09.00.41.08', false)], null);

        // assert
        expect(candidates).toHaveLength(0);
    });

    it('should rank HW VP9 by efficiency ahead of HEVC and H.264', async () => {
        // arrange
        const cs = await importCodecSupport();
        const supported = [
            codecInfo('h264', 'avc1.640029', true),
            codecInfo('hevc', 'hev1.1.6.L120.B0', true),
            codecInfo('vp9', 'vp09.00.41.08', true),
        ];

        // act
        const candidates = cs.listEncoderCandidatesByEfficiency(supported, null);

        // assert
        expect(candidates.map(c => c.category)).toEqual(['vp9', 'hevc', 'h264']);
    });

    it('should respect the allowed-category set from the audience', async () => {
        // arrange
        const cs = await importCodecSupport();
        const supported = [
            codecInfo('h264', 'avc1.640029', true),
            codecInfo('vp9', 'vp09.00.41.08', true),
        ];

        // act
        const candidates = cs.listEncoderCandidatesByEfficiency(supported, new Set(['h264']));

        // assert
        expect(candidates.map(c => c.category)).toEqual(['h264']);
    });
});
