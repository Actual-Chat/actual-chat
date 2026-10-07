import { describe, it, expect, vi, afterEach } from 'vitest';
import { AsyncLock, PromiseSource } from 'actuallab-core';
import { VideoRecorder } from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/video-recorder';
import type { LayerConfig } from '../../../src/dotnet/UI.Blazor.App/Components/VideoPanel/layer-ladder';
import { detectSupportedCodecs } from '../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support';
import { MediaCapture } from '../../../src/dotnet/UI.Blazor.App/Services/Video/services/media-capture';

vi.mock('app-constants', async importOriginal => ({
    ...await importOriginal<typeof import('app-constants')>(),
    VIDEO: {
        frameRate: 30,
        mobileFrameRate: 30,
        cameraLayerBaseBitratesKbps: [200, 500, 1500, 2500],
        screenCastLayerBaseBitratesKbps: [500, 1500],
    },
}));
vi.mock('api', () => ({ Api: {}, WorkerKind: {} }));
vi.mock('versioning', () => ({ Versioning: {} }));
vi.mock('device-info', () => ({ DeviceInfo: {} }));
vi.mock('orientation', () => ({ ScreenOrientation: {} }));
vi.mock('shared-settings', () => ({ SharedSettings: {} }));
vi.mock('shared-settings-worker', () => ({ SharedSettingsWorkerSync: {} }));
vi.mock('../../../src/dotnet/UI.Blazor/Services/BrowserInit/browser-init', () => ({ BrowserInit: {} }));
vi.mock('../../../src/dotnet/UI.Blazor/Services/BrowserInfo/browser-info', () => ({ BrowserInfo: {} }));
vi.mock('../../../src/dotnet/UI.Blazor/Services/ConnectivityUI/connectivity-ui',
    () => ({ ConnectivityUI: {} }));
vi.mock('../../../src/dotnet/UI.Blazor.App/Services/Video/services/media-capture', () => ({
    MediaCapture: {
        captureCameraStream: vi.fn(),
        captureScreenCast: vi.fn(),
        discardPendingScreenCast: vi.fn(),
    },
}));
vi.mock('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support', async importOriginal => ({
    ...await importOriginal<typeof import('../../../src/dotnet/UI.Blazor.App/Services/Video/codec-support')>(),
    detectSupportedCodecs: vi.fn(),
}));

afterEach(() => {
    vi.unstubAllGlobals();
    vi.clearAllMocks();
});

interface WorkerStub {
    stop(): Promise<void>;
}

interface RecorderHarness {
    lifecycleLock: AsyncLock;
    lifecycleEpoch: number;
    worker: WorkerStub | null;
    inputTrack: MediaStreamTrack | null;
    isRecording: boolean;
    isStoppingRecording: boolean;
    disposed: boolean;
    _recordingState: string;
    isScreenCasting: boolean;
    blazorRef: { invokeMethodAsync: ReturnType<typeof vi.fn> };
    setRecordingState: ReturnType<typeof vi.fn>;
    cleanupPreviewTrack: ReturnType<typeof vi.fn>;
    resetDemandState: ReturnType<typeof vi.fn>;
    unregister: ReturnType<typeof vi.fn>;
    extractEncoderCategories: ReturnType<typeof vi.fn>;
    pickInitialCodec: ReturnType<typeof vi.fn>;
    pickSimulcastCodec: ReturnType<typeof vi.fn<() => Promise<{ codec: string; accel: string }>>>;
    pickAccelerationFor: ReturnType<typeof vi.fn>;
    withCodecBitrates: ReturnType<typeof vi.fn>;
    adoptGrantedCamera: ReturnType<typeof vi.fn>;
    watchScreenSize: ReturnType<typeof vi.fn<() => Promise<{ width: number; height: number }>>>;
    buildScreenCastLadder: ReturnType<typeof vi.fn>;
    resolveActiveLadder: ReturnType<typeof vi.fn>;
    startWorker: ReturnType<typeof vi.fn<() => Promise<void>>>;
    tearDownWorkerSource: ReturnType<typeof vi.fn>;
    tearDownWorker: ReturnType<typeof vi.fn>;
    ensureWorker: ReturnType<typeof vi.fn>;
    restartWithCurrentConfig(): Promise<void>;
    stopWorkerForRestart(prefix: string): Promise<boolean>;
    stopRecording(): Promise<void>;
    startRecording(chatId: string, audienceCodecs?: string[], maxLayerCount?: number): Promise<void>;
    startScreenCast(chatId: string, audienceCodecs?: string[], maxLayerCount?: number): Promise<void>;
    warmup(chatId: string, audienceCodecs?: string[]): Promise<void>;
}

function createHarness(worker: WorkerStub): RecorderHarness {
    const recorder = Object.create(VideoRecorder.prototype) as RecorderHarness;
    recorder.lifecycleLock = new AsyncLock();
    recorder.lifecycleEpoch = 0;
    recorder.worker = worker;
    recorder.inputTrack = { stop: vi.fn() } as unknown as MediaStreamTrack;
    recorder.isRecording = true;
    recorder.isStoppingRecording = false;
    recorder.disposed = false;
    recorder._recordingState = 'recording';
    recorder.isScreenCasting = false;
    recorder.blazorRef = { invokeMethodAsync: vi.fn(() => Promise.resolve()) };
    recorder.setRecordingState = vi.fn((state: string) => { recorder._recordingState = state; });
    recorder.cleanupPreviewTrack = vi.fn(() => {
        recorder.inputTrack?.stop();
        recorder.inputTrack = null;
    });
    recorder.resetDemandState = vi.fn();
    recorder.unregister = vi.fn();
    recorder.extractEncoderCategories = vi.fn(() => []);
    recorder.pickInitialCodec = vi.fn(() => 'vp09.00.10.08');
    recorder.pickSimulcastCodec = vi.fn(() => Promise.resolve({ codec: 'vp09.00.10.08', accel: 'no-preference' }));
    recorder.pickAccelerationFor = vi.fn(() => 'no-preference');
    recorder.withCodecBitrates = vi.fn((ladder: LayerConfig[]) => ladder);
    recorder.adoptGrantedCamera = vi.fn(() => false);
    recorder.watchScreenSize = vi.fn(() => Promise.resolve({ width: 640, height: 360 }));
    recorder.buildScreenCastLadder = vi.fn(() => [{ width: 640, height: 360, bitrateKbps: 500 }]);
    recorder.resolveActiveLadder = vi.fn(() => [{ width: 640, height: 360 }]);
    recorder.startWorker = vi.fn(() => Promise.resolve());
    recorder.tearDownWorkerSource = vi.fn();
    recorder.tearDownWorker = vi.fn(() => { recorder.worker = null; });
    recorder.ensureWorker = vi.fn(() => { recorder.worker = { stop: () => Promise.resolve() }; });
    return recorder;
}

describe('video recorder lifecycle', () => {
    it('should not start publishing after stop invalidates asynchronous source setup', async () => {
        const whenSourceSet = new PromiseSource<void>();
        const whenSettingSource = new PromiseSource<void>();
        const worker = {
            stop: () => Promise.resolve(),
            setSource: vi.fn(() => {
                whenSettingSource.resolve();
                return whenSourceSet;
            }),
            start: vi.fn(),
        };
        const recorder = createHarness(worker);
        vi.stubGlobal('MediaStreamTrackProcessor', class {
            readable = new ReadableStream();
        });
        const prototype = VideoRecorder.prototype as unknown as {
            startWorker(ladder: { width: number; height: number }[]): Promise<void>;
        };
        const whenStarted = prototype.startWorker.call(recorder, [{ width: 640, height: 360 }]) as Promise<void>;
        await whenSettingSource;

        recorder.isRecording = false;
        recorder._recordingState = 'starting';
        await recorder.stopRecording();
        whenSourceSet.resolve();
        await whenStarted;

        expect(worker.start).not.toHaveBeenCalled();
    });

    it.each([
        ['camera', 'detection'], ['camera', 'probe'], ['camera', 'capture'], ['camera', 'worker'],
        ['screen', 'detection'], ['screen', 'capture'], ['screen', 'size'], ['screen', 'worker'],
        ['warmup', 'detection'], ['warmup', 'capture'], ['warmup', 'worker'],
    ] as const)('should cancel %s startup during %s through Stop', async (mode, stage) => {
        const whenPaused = new PromiseSource<void>();
        const whenResumed = new PromiseSource<void>();
        const recorder = createHarness({ stop: () => Promise.resolve() });
        const stopTrack = vi.fn();
        const track = {
            stop: stopTrack,
            getSettings: () => ({ width: 640, height: 360, frameRate: 30 }),
        } as unknown as MediaStreamTrack;
        recorder.isRecording = false;
        recorder.inputTrack = null;
        recorder._recordingState = 'stopped';
        const pauseAt = async (step: string): Promise<void> => {
            if (step !== stage)
                return;

            whenPaused.resolve();
            await whenResumed;
        };
        vi.mocked(detectSupportedCodecs).mockImplementation(async () => {
            await pauseAt('detection');
            return [];
        });
        const capture = async (): Promise<MediaStreamTrack> => {
            await pauseAt('capture');
            return track;
        };
        const captureCamera = vi.spyOn(MediaCapture, 'captureCameraStream').mockImplementation(capture);
        const captureScreen = vi.spyOn(MediaCapture, 'captureScreenCast').mockImplementation(capture);
        const discardPending = vi.spyOn(MediaCapture, 'discardPendingScreenCast');
        recorder.pickSimulcastCodec.mockImplementation(async () => {
            await pauseAt('probe');
            return { codec: 'vp09.00.10.08', accel: 'no-preference' };
        });
        recorder.watchScreenSize.mockImplementation(async () => {
            await pauseAt('size');
            return { width: 640, height: 360 };
        });
        recorder.startWorker.mockImplementation(() => pauseAt('worker'));

        const whenStarted = mode === 'camera'
            ? recorder.startRecording('chat', ['vp9'], 1)
            : mode === 'screen' ? recorder.startScreenCast('chat', ['vp9'], 1) : recorder.warmup('chat', ['vp9']);
        await whenPaused;
        await recorder.stopRecording();
        whenResumed.resolve();
        await whenStarted;

        expect(recorder.isRecording).toBe(false);
        expect(recorder.isScreenCasting).toBe(false);
        expect(recorder._recordingState).toBe('stopped');
        expect(recorder.inputTrack).toBeNull();
        expect(recorder.blazorRef.invokeMethodAsync).not.toHaveBeenCalledWith('OnRecordingStarted');
        expect(recorder.blazorRef.invokeMethodAsync).toHaveBeenCalledWith('OnRecordingStopped');
        expect(discardPending).toHaveBeenCalledTimes(mode === 'screen' ? 1 : 0);
        if (stage === 'detection' || stage === 'probe') {
            expect(captureCamera).not.toHaveBeenCalled();
            expect(captureScreen).not.toHaveBeenCalled();
        } else {
            expect(stopTrack).toHaveBeenCalledTimes(1);
        }
    });

    it.each(['camera', 'screen', 'warmup'] as const)(
        'should ignore a late %s startup error after Stop', async mode => {
            const whenDetecting = new PromiseSource<void>();
            const whenDetected = new PromiseSource<never>();
            const recorder = createHarness({ stop: () => Promise.resolve() });
            recorder.isRecording = false;
            recorder._recordingState = 'stopped';
            vi.mocked(detectSupportedCodecs).mockImplementation(() => {
                whenDetecting.resolve();
                return whenDetected;
            });

            const whenStarted = mode === 'camera'
                ? recorder.startRecording('chat', ['vp9'], 1)
                : mode === 'screen' ? recorder.startScreenCast('chat', ['vp9'], 1) : recorder.warmup('chat', ['vp9']);
            await whenDetecting;
            await recorder.stopRecording();
            whenDetected.reject(new Error('obsolete detection failed'));
            await whenStarted;

            expect(recorder._recordingState).toBe('stopped');
            expect(recorder.setRecordingState).not.toHaveBeenCalledWith('error');
            expect(recorder.blazorRef.invokeMethodAsync).toHaveBeenCalledExactlyOnceWith('OnRecordingStopped');
        });

    it.each(['camera', 'screen', 'warmup'] as const)(
        'should preserve a new %s session when cancelled capture completes late', async mode => {
            const whenCapturing = new PromiseSource<void>();
            const whenCaptured = new PromiseSource<MediaStreamTrack>();
            const recorder = createHarness({ stop: () => Promise.resolve() });
            const stopOldTrack = vi.fn();
            const stopNewTrack = vi.fn();
            const oldTrack = { stop: stopOldTrack } as unknown as MediaStreamTrack;
            const newTrack = {
                stop: stopNewTrack,
                getSettings: () => ({ width: 640, height: 360, frameRate: 30 }),
            } as unknown as MediaStreamTrack;
            recorder.isRecording = false;
            recorder.inputTrack = null;
            recorder._recordingState = 'stopped';
            vi.mocked(detectSupportedCodecs).mockResolvedValue([]);
            const capture = vi.fn<() => Promise<MediaStreamTrack>>()
                .mockImplementationOnce(() => {
                    whenCapturing.resolve();
                    return whenCaptured;
                })
                .mockResolvedValue(newTrack);
            vi.spyOn(MediaCapture, 'captureCameraStream').mockImplementation(capture);
            vi.spyOn(MediaCapture, 'captureScreenCast').mockImplementation(capture);
            const start = (): Promise<void> => mode === 'camera'
                ? recorder.startRecording('chat', ['vp9'], 1)
                : mode === 'screen' ? recorder.startScreenCast('chat', ['vp9'], 1) : recorder.warmup('chat', ['vp9']);

            const whenOldStarted = start();
            await whenCapturing;
            await recorder.stopRecording();
            await start();
            whenCaptured.resolve(oldTrack);
            await whenOldStarted;

            expect(recorder.isRecording).toBe(true);
            expect(recorder.inputTrack).toBe(newTrack);
            expect(recorder._recordingState).toBe(mode === 'warmup' ? 'warming-up' : 'recording');
            expect(stopOldTrack).toHaveBeenCalledTimes(1);
            expect(stopNewTrack).not.toHaveBeenCalled();
        });

    it('should serialize overlapping restart requests', async () => {
        const whenStopped = new PromiseSource<void>();
        const whenStopping = new PromiseSource<void>();
        const stop = vi.fn(() => {
            whenStopping.resolve();
            return whenStopped;
        });
        const recorder = createHarness({ stop });
        const firstRestart = recorder.restartWithCurrentConfig();
        await whenStopping;
        const secondRestart = recorder.restartWithCurrentConfig();

        expect(stop).toHaveBeenCalledTimes(1);
        whenStopped.reject(new Error('stop timed out'));
        await Promise.all([firstRestart, secondRestart]);

        expect(recorder.tearDownWorker).toHaveBeenCalledTimes(1);
        expect(recorder.ensureWorker).toHaveBeenCalledTimes(1);
        expect(recorder.startWorker).toHaveBeenCalledTimes(2);
    });

    it('should not replace or restart a worker when stop supersedes recovery', async () => {
        const whenStopped = new PromiseSource<void>();
        const whenStopping = new PromiseSource<void>();
        const recorder = createHarness({ stop: () => {
            whenStopping.resolve();
            return whenStopped;
        } });
        const whenRestarted = recorder.restartWithCurrentConfig();
        await whenStopping;

        const whenRecordingStopped = recorder.stopRecording();
        whenStopped.reject(new Error('stop timed out'));
        await Promise.all([whenRestarted, whenRecordingStopped]);

        expect(recorder.tearDownWorker).toHaveBeenCalledTimes(1);
        expect(recorder.isRecording).toBe(false);
        expect(recorder.ensureWorker).not.toHaveBeenCalled();
        expect(recorder.startWorker).not.toHaveBeenCalled();
    });

    it('should not destroy a replacement when an obsolete worker stop fails', async () => {
        const whenStopped = new PromiseSource<void>();
        const recorder = createHarness({ stop: () => whenStopped });
        const whenRestartAllowed = recorder.stopWorkerForRestart('test');
        const replacement = { stop: () => Promise.resolve() };
        recorder.worker = replacement;

        whenStopped.reject(new Error('old stop timed out'));

        expect(await whenRestartAllowed).toBe(false);
        expect(recorder.worker).toBe(replacement);
        expect(recorder.tearDownWorker).not.toHaveBeenCalled();
    });

    it('should discard queued restarts from a previous recording session', async () => {
        const recorder = createHarness({ stop: () => Promise.resolve() });
        await recorder.lifecycleLock.acquire();
        const whenRestarted = recorder.restartWithCurrentConfig();
        recorder.lifecycleEpoch++;

        recorder.lifecycleLock.release();
        await whenRestarted;

        expect(recorder.startWorker).not.toHaveBeenCalled();
    });
});
