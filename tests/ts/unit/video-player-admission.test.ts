import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PromiseSource } from 'actuallab-core';

const mocks = vi.hoisted(() => ({
    detectCodecs: vi.fn<() => Promise<string[]>>(),
    floorCodec: vi.fn(() => 'vp09.00.41.08'),
    candidates: vi.fn(() => ['vp09.00.41.08']),
    selectCodec: vi.fn<() => Promise<null>>(),
}));
vi.mock('api', () => ({ Api: { releaseConnection: vi.fn() }, streamingApi: {} }));
vi.mock('versioning', () => ({ Versioning: {} }));
vi.mock('app-constants', () => {
    const video = { targetBufferSpanMs: 120 };
    return { AC: { video }, VIDEO: video };
});
vi.mock('event-handling', async (importOriginal) => ({
    ...await importOriginal<typeof import('event-handling')>(),
    DocumentEvents: {},
}));
vi.mock('../../../src/dotnet/UI.Blazor/Services/BrowserInit/browser-init',
    () => ({ BrowserInit: {} }));
vi.mock('../../../src/dotnet/UI.Blazor/Services/ConnectivityUI/connectivity-ui',
    () => ({ ConnectivityUI: {} }));
vi.mock('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support', () => ({
    detectSupportedDecoderCodecs: mocks.detectCodecs,
    getCodecForCategory: mocks.floorCodec,
    isDecoderCodecProven: vi.fn(),
    markDecoderCodecProven: vi.fn(),
}));

vi.mock('../../../src/dotnet/UI.Blazor.App/Services/Video/hevc-codec-selection', () => ({
    getCodecCandidates: mocks.candidates,
    selectDecoderCodec: mocks.selectCodec,
}));

import { VideoPlayer } from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-player';

interface TestPlayer {
    stop(): Promise<void>;
    startWorkerForAttempt(streamId: string): Promise<void>;
    initPlayerWorker(codec: string, width: number, height: number, settings: string): Promise<void>;
    isPlaying: boolean;
    playerWorker: { start: ReturnType<typeof vi.fn>; dispose: ReturnType<typeof vi.fn> } | null;
}

function createPlayer(): TestPlayer {
    return Object.assign(Object.create(VideoPlayer.prototype) as TestPlayer, {
        isPlaying: true,
        streamId: 'stream',
        selectedCodec: 'vp09.00.10.08',
        playerWorker: { start: vi.fn(), dispose: vi.fn() },
        audioCaTimer: null,
        livenessTimer: null,
        viewportCheckRafHandle: null,
        dropPerSec: new Map(),
        lastDropAtTick: new Map(),
        bytesSamples: [],
        decodeDeficitTicker: { reset: vi.fn() },
        renderBackend: { isOffThread: false, dispose: vi.fn() },
        transferCanvasToOffscreen: vi.fn(),
        getExpectedPaused: vi.fn(() => false),
        supportsWebCodecs: vi.fn(() => true),
        reportEnded: vi.fn(() => Promise.resolve()),
    });
}

beforeEach(() => {
    mocks.detectCodecs.mockReset();
    mocks.floorCodec.mockClear();
    mocks.candidates.mockClear();
    mocks.selectCodec.mockReset().mockResolvedValue(null);
});

describe('video player capability admission', () => {
    it('should probe a floor codec while an incompatible publisher switches streams', async () => {
        const player = createPlayer();

        await player.initPlayerWorker('vp9', 1920, 1080, '');

        expect(mocks.floorCodec).toHaveBeenCalledWith('vp9', 1920, 1080);
        expect(mocks.candidates).toHaveBeenCalledWith('vp09.00.41.08', undefined);
        expect(mocks.selectCodec).toHaveBeenCalledWith(['vp09.00.41.08'], undefined, { width: 1920, height: 1080 });
    });

    it('should not start a playback worker after Stop during capability detection', async () => {
        const capabilities = new PromiseSource<string[]>();
        mocks.detectCodecs.mockReturnValue(capabilities.promise);
        const player = createPlayer();
        const worker = player.playerWorker!;
        const start = player.startWorkerForAttempt('stream');

        await player.stop();
        capabilities.resolve(['vp9']);
        await start;

        expect(player.isPlaying).toBe(false);
        expect(worker.dispose).toHaveBeenCalledOnce();
        expect(worker.start).not.toHaveBeenCalled();
    });

    it('should not start an obsolete worker after capability detection', async () => {
        const capabilities = new PromiseSource<string[]>();
        mocks.detectCodecs.mockReturnValue(capabilities.promise);
        const player = createPlayer();
        const worker = player.playerWorker!;
        const start = player.startWorkerForAttempt('stream');
        player.playerWorker = { start: vi.fn(), dispose: vi.fn() };

        capabilities.resolve(['vp9']);
        await start;

        expect(worker.start).not.toHaveBeenCalled();
        expect(player.playerWorker.start).not.toHaveBeenCalled();
    });
});
