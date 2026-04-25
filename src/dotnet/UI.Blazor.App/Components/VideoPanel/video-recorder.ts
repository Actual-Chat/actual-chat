import { getLogs } from 'logging';
import { DeviceInfo } from 'device-info';
import { RecordingService, type RecordingConfig, type RecordingState } from '../../Services/Video/services/recording-service';
import type { SpatialLayerConfig } from '../../Services/Video/workers/video-processing-worker-contract';
import { detectSupportedCodecs, getDefaultCodec, getCodecCategory, probeConcurrentEncoders, type CodecInfo } from '../../Services/Video/codec-support';
import { getExpectedBitrate } from '../../Services/Video/bitrate-table';
import { hasHigherTopTier, maybeAugmentLadderForRunningBase } from './simulcast-ladder';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoRecorder');

export interface OwnStreamDiagnostics {
    mode: string;
    codec: string;
    codecCategory: string;
    hardwareAccelerated: boolean;
    inputResolution: string;
    inputFramerate: number;
    outputResolution: string;
    configuredBitrate: number;
    actualBitrateKbps: number;
    encodedFrames: number;
    droppedFrames: number;
    keyFrames: number;
    medianEncodeTime: number;
    pureMedianEncodeTime: number;
    encoderHwAccel: string;
    duration: number;
    cameraLabel: string | null;
    blurEnabled: boolean;
    segmentationBackend: string | null;
    segmentationAvgTime: number | null;
    supportedEncoderCategories: string[];
    status: string;
    orientation: {
        firstDisplayResolution: string;
        firstCodedResolution: string;
        firstRotation: string;
        lastRotation: string;
        configuredResolution: string;
        needsRotation: boolean;
        rotationDetection: string;
        framesSeen: number;
    } | null;
}

export interface VideoDevice {
    deviceId: string;
    label: string;
}

// Module-level registry keyed by StreamKind so a user can simultaneously
// stream webcam (kind=0) and screencast (kind=1). Callers that want the
// webcam-specific recorder (preview, modal, diagnostics) pass kind=0
// (the default). Kinds match the C# StreamKind enum values.
const StreamKindWebcam = 0;
const activeRecorders = new Map<number, VideoRecorder>();

export function getActiveRecorder(kind: number = StreamKindWebcam): VideoRecorder | null {
    return activeRecorders.get(kind) ?? null;
}

export function getAllActiveRecorders(): VideoRecorder[] {
    return [...activeRecorders.values()];
}

export type ActiveRecorderListener = (recorder: VideoRecorder | null, kind: number) => void;

const registryListeners = new Set<ActiveRecorderListener>();

// Subscribe to active-recorder registry changes. Callback fires with the
// newly-registered recorder (or null on unregister) + its kind. Returns an
// unsubscribe closure. Consumers typically filter by kind inside the callback.
export function addActiveRecorderListener(cb: ActiveRecorderListener): () => void {
    registryListeners.add(cb);
    return () => registryListeners.delete(cb);
}

function notifyRegistryListeners(recorder: VideoRecorder | null, kind: number): void {
    for (const cb of registryListeners) {
        try {
            cb(recorder, kind);
        } catch (e) {
            warnLog?.log('active-recorder listener threw', e);
        }
    }
}

interface Size {
    width: number;
    height: number;
}

/**
 * See {@link VideoRecorder.addPreviewFrameListener} for the listener contract.
 */
export type PreviewFrameListener = (frame: VideoFrame) => void;

export type VideoRecordingState = 'stopped' | 'starting' | 'recording' | 'error';

export class VideoRecorder {
    private blazorRef: DotNet.DotNetObject;
    // Video recording service (using video-pipeline)
    private recordingService: RecordingService | null = null;
    private isRecording = false;
    // True when we were asked to record but currently have no active pipeline
    // (e.g. the user switched to a camera that failed to start). The next
    // switchCamera call restarts from this state.
    private isScreencasting = false;
    // StreamKind this instance is registered under. Set in the constructor and
    // cleared on dispose (we register immediately so VideoStreamingPreview can
    // see the recorder during the pipeline startup phase, not only after the
    // first frame lands).
    private registeredKind: number | null = null;
    private previewTrack: MediaStreamTrack | null = null;
    private selectedCameraDeviceId: string | null = null;
    private chatId = '';
    private isBlurEnabled = false;
    private blurToggleChain: Promise<void> = Promise.resolve();
    private disposed = false;
    private lastStatus = '';
    private cameraWidth = 0;
    private cameraHeight = 0;
    // Cached encoder capabilities (detected at recording start)
    private supportedEncoderCategories: string[] = [];
    private audienceCodecs?: string[];
    private supportedCodecs: CodecInfo[] = [];
    // Simulcast layer ladder — cached; applied to the next startRecording. Empty/null
    // = single-encoder (P2P) mode. Index 0 is the base layer (lowest res); higher
    // indices are enhancement layers. Sent by C# VideoRecorder in response to
    // VideoQualityPreset.MaxSpatialLayer aggregate changes.
    private simulcastLayers: SpatialLayerConfig[] | null = null;

    // Blur preview frame subscribers. When blur is active, the pipeline produces
    // `VideoFrame`s that we dispatch to every listener before the frame is closed
    // by the pipeline. Listeners MUST consume the frame synchronously (draw it to
    // a canvas before returning); they must NOT close it and must NOT retain it.
    private previewFrameListeners = new Set<PreviewFrameListener>();

    // Lifecycle change subscribers. Each set is fired from the matching private
    // setter below when the tracked value flips. Listeners may throw — we catch
    // and log so one bad handler can't break the rest.
    private stateChangeListeners = new Set<(state: VideoRecordingState) => void>();
    private blurChangeListeners = new Set<(enabled: boolean) => void>();

    private _recordingState: VideoRecordingState = 'stopped';
    public get recordingState(): VideoRecordingState { return this._recordingState; }

    static create(blazorRef: DotNet.DotNetObject, kind: number): VideoRecorder {
        return new VideoRecorder(blazorRef, kind);
    }

    static async enumerateDevices(): Promise<VideoDevice[]> {
        try {
            // Request permission first to get device labels
            const tempStream = await navigator.mediaDevices.getUserMedia({ video: true });
            tempStream.getTracks().forEach(t => t.stop());

            const devices = await navigator.mediaDevices.enumerateDevices();
            const videoInputs = devices.filter(d => d.kind === 'videoinput');

            // On mobile, browsers typically expose multiple physical back cameras
            // (wide, ultra-wide, telephoto). The UI only needs "front" and "back",
            // so pick a single device per facing mode.
            const selected = DeviceInfo.isMobile
                ? VideoRecorder.pickMobileCameras(videoInputs)
                : videoInputs;

            const videoDevices = selected.map(d => ({
                deviceId: d.deviceId,
                label: d.label || `Camera ${d.deviceId.slice(0, 8)}`,
            }));
            infoLog?.log('Enumerated video devices:', videoDevices);
            return videoDevices;
        } catch (error) {
            errorLog?.log('Failed to enumerate video devices:', error);
            return [];
        }
    }

    /**
     * Reduces a mobile device's camera list to at most one front + one back camera.
     * Uses {@link InputDeviceInfo.getCapabilities} when available, falls back to
     * a label heuristic (Chrome on Android labels them "... facing front/back").
     */
    private static pickMobileCameras(devices: MediaDeviceInfo[]): MediaDeviceInfo[] {
        const facingOf = (d: MediaDeviceInfo): 'user' | 'environment' | null => {
            // getCapabilities may be absent in older browsers even though TS types
            // declare it as always present on InputDeviceInfo.
            const input = d as InputDeviceInfo;
            const facing: string[] | undefined = typeof input.getCapabilities === 'function'
                ? input.getCapabilities().facingMode
                : undefined;
            if (facing && facing.length > 0) {
                if (facing.includes('user')) return 'user';
                if (facing.includes('environment')) return 'environment';
            }
            const label = d.label.toLowerCase();
            if (/facing front|\bfront\b|\buser\b|self/.test(label)) return 'user';
            if (/facing back|\bback\b|\brear\b|environment/.test(label)) return 'environment';
            return null;
        };

        const front = devices.find(d => facingOf(d) === 'user');
        const back = devices.find(d => facingOf(d) === 'environment');
        if (front && back)
            return [front, back];
        if (front || back)
            // Only one facing mode could be identified; return it plus the first
            // unidentified device (if any) as a best-effort second camera.
            return [front ?? back!, ...devices.filter(d => d !== (front ?? back) && facingOf(d) === null).slice(0, 1)];

        // No facing mode info at all — just cap the list at 2 devices.
        return devices.slice(0, 2);
    }

    constructor(blazorRef: DotNet.DotNetObject, kind: number) {
        this.blazorRef = blazorRef;
        this.register(kind);
    }

    /**
     * Set the selected camera device
     */
    public setSelectedCamera(deviceId: string): void {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Selected camera device:', deviceId);
    }

    /**
     * Switch camera during active recording by stopping and restarting with the new device.
     *
     * If the new camera fails to start (e.g. a ghost device registered in the OS but not
     * delivering frames), startRecording's error path leaves us in the interrupted state
     * (`isInterrupted = true`, `recordingService = null`). The Blazor side no longer
     * tears the panel down on OnRecordingError, so the user can simply click switch
     * camera again — the next call will fall through the stop branch (pipeline is
     * already null) and try startRecording with the new device.
     */
    public async switchCamera(deviceId: string): Promise<void> {
        this.selectedCameraDeviceId = deviceId;
        infoLog?.log('Switching camera to:', deviceId);

        // Never asked to record yet — just remember the device for the next start
        if (!this.chatId) {
            infoLog?.log('Not yet recording — camera will be used on next start');
            return;
        }

        // Tear down the current pipeline if one exists
        if (this.recordingService) {
            this.cleanupPreviewTrack();
            try {
                await this.recordingService.stop();
            } catch (e) {
                warnLog?.log('Stop during switch failed:', e);
            }
            this.recordingService = null;
            this.isRecording = false;
            this.setRecordingState('stopped');
        }

        await this.startRecording(this.chatId, this.audienceCodecs);
    }

    /**
     * Set whether background blur should be enabled when recording starts
     */
    public setBlurEnabled(enabled: boolean): void {
        this.setIsBlurEnabled(enabled);
        infoLog?.log('Background blur enabled:', enabled);
    }

    // Stores the simulcast ladder. Applied immediately to a running pipeline
    // via hot encoder reconfig (Option C: see RecordingService.setSimulcastLadder
    // → VideoPipeline.setSpatialLayers → worker.setSpatialLayers). The hot
    // path swaps extras without restarting the base encoder or the RPC
    // PushVideo stream, so other peers don't see this peer's stream churn —
    // avoiding the cascading stop/start loop documented in commit 2de3f2617.
    // The cache is also picked up by the next fresh startRecording so a stop
    // → start cycle preserves the latest ladder. Passing null or a list of
    // length < 2 disables simulcast. Screencast streams ignore the ladder
    // (single-encoder text legibility path).
    public setSimulcastLayers(layers: SpatialLayerConfig[] | null, force = false): void {
        const incoming = (layers && layers.length >= 2) ? layers : null;
        const prevCount = this.simulcastLayers?.length ?? 0;
        // Bug N: don't downgrade a probe-promoted ladder. C#'s default ladder
        // tops out at 720p; the JS-side startRecording probe may have promoted
        // it to 1080p. If C# later pushes its default 720p-cap ladder
        // (e.g. SyncRemoteStreamCount activating), we'd lose the 1080p tier.
        // Only accept the incoming ladder when it's null (deactivate), strictly
        // longer than ours, OR has a strictly higher top tier.
        //
        // `force = true` bypasses this guard. Used by C#-side cap-driven shrinks
        // (G1: server's MaxSpatialLayer aggregate dropped because all viewers
        // want low/mid). Intent is explicit, downgrade is the policy.
        let active: SpatialLayerConfig[] | null;
        if (incoming === null) {
            active = null;
        } else if (force
            || this.simulcastLayers === null
            || incoming.length > this.simulcastLayers.length
            || hasHigherTopTier(incoming, this.simulcastLayers)) {
            active = incoming;
            if (force && this.simulcastLayers !== null && incoming.length < this.simulcastLayers.length) {
                infoLog?.log(
                    `setSimulcastLayers: force-shrink ${this.simulcastLayers.length} → ${incoming.length} layer(s)`);
            }
        } else {
            active = this.simulcastLayers;
            infoLog?.log(
                `setSimulcastLayers: keeping promoted ladder (${prevCount} layers, top=${this.simulcastLayers[this.simulcastLayers.length - 1].width}x${this.simulcastLayers[this.simulcastLayers.length - 1].height}) over incoming (${incoming.length} layers)`);
        }
        // Bug AA defensive: hot setSpatialLayers leaves the base encoder running
        // at its current resolution, so the wire layout is `[base, ...extras]`
        // — and if the base is *bigger* than every incoming extra, the extras
        // numbering doesn't reach the receiver-side filter's "top" (which sorts
        // by spatial-id, not pixel count). Augment the ladder with a tier
        // matching the running base so the worker creates an extra at the same
        // resolution; the duplicate 1080p encoder costs bandwidth, but it gives
        // the filter a spatial-id high enough to reach 1080p in delivery.
        // Reads from getEncoderStats so a prior reconfigure() (server-driven
        // step-down) is reflected — RecordingConfig.width/height stays stale
        // because reconfigure() doesn't write back to it.
        if (active !== null && this.recordingService) {
            const stats = this.recordingService.getPipeline()?.getEncoderStats();
            const runningH = stats?.configuredHeight ?? 0;
            const runningW = stats?.configuredWidth ?? 0;
            const cfg = this.recordingService.getConfig();
            const codec = cfg.codecString ?? '';
            const runningBase = runningH > 0
                ? { width: runningW, height: runningH, bitrate: getExpectedBitrate(codec, runningH, cfg.mode) }
                : null;
            const augmented = maybeAugmentLadderForRunningBase(active, runningBase);
            if (augmented.length > active.length) {
                infoLog?.log(
                    `setSimulcastLayers: augmented ladder with running base tier ${runningW}x${runningH} (running > incoming top ${active[active.length - 1].width}x${active[active.length - 1].height})`);
            }
            active = augmented;
        }
        const newCount = active?.length ?? 0;
        this.simulcastLayers = active;
        if (prevCount !== newCount) {
            infoLog?.log(`setSimulcastLayers: ${prevCount} -> ${newCount} layer(s)`);
        }
        if (this.recordingService) {
            // Hot apply to the running pipeline. Failures are non-fatal — the
            // ladder remains cached and applies at the next fresh start.
            void this.recordingService.setSimulcastLadder(active).catch((e: unknown) =>
                warnLog?.log('setSimulcastLayers: hot reconfig failed:', e));
        }
    }

    public setRemoteStreamCount(count: number): void {
        this.recordingService?.getPipeline()?.setRemoteStreamCount(count);
    }

    /**
     * Toggle blur on an active recording
     */
    public toggleBlur(enabled: boolean): void {
        this.setIsBlurEnabled(enabled);
        if (this.recordingService) {
            const rs = this.recordingService;
            this.blurToggleChain = this.blurToggleChain
                .then(() => rs.toggleBlur(enabled))
                .catch((e: unknown) => warnLog?.log('Failed to toggle blur:', e));
        }
    }

    /**
     * Get the raw preview track for rendering by VideoStreamingPreview.
     */
    public getPreviewTrack(): MediaStreamTrack | null {
        return this.previewTrack;
    }

    /**
     * Get the device ID of the currently selected camera.
     */
    public getPreviewDeviceId(): string | null {
        return this.selectedCameraDeviceId;
    }

    /**
     * Whether blur is currently active on this recorder.
     */
    public isBlurActive(): boolean {
        return this.isBlurEnabled;
    }

    /**
     * Whether this recorder is currently in screencast mode.
     */
    public isScreencastActive(): boolean {
        return this.isScreencasting;
    }

    /**
     * Subscribe to blur preview frames produced by the recorder's pipeline.
     * The returned function unsubscribes the listener.
     *
     * Listener contract:
     *  - called synchronously from the pipeline, once per produced frame;
     *  - MUST consume the frame within the callback (e.g. `ctx.drawImage(frame, ...)`);
     *  - MUST NOT call `frame.close()` (ownership stays with the pipeline);
     *  - MUST NOT retain the frame (the pipeline closes it after dispatch);
     *  - only fires while `isBlurActive()` is true.
     */
    public addPreviewFrameListener(cb: PreviewFrameListener): () => void {
        this.previewFrameListeners.add(cb);
        return () => this.previewFrameListeners.delete(cb);
    }

    // Subscribe to recordingState transitions. Fires with the new state after
    // each flip (no initial fire). Returns an unsubscribe closure.
    // External track death (permission revoked, camera unplugged) is surfaced
    // via this listener too — the track's `onended` handler triggers
    // `stopRecording()`, which flips state to `'stopped'`.
    public addStateChangeListener(cb: (state: VideoRecordingState) => void): () => void {
        this.stateChangeListeners.add(cb);
        return () => this.stateChangeListeners.delete(cb);
    }

    // Subscribe to blur on/off flips.
    public addBlurChangeListener(cb: (enabled: boolean) => void): () => void {
        this.blurChangeListeners.add(cb);
        return () => this.blurChangeListeners.delete(cb);
    }

    /**
     * Initialize and start video recording
     */
    public async startRecording(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        infoLog?.log('Starting video recording...');

        try {
            // Capture at 720p on all platforms — lower resolutions may select the wrong
            // camera on Android and produce aspect-ratio mismatches.
            const targetSize = { width: 1280, height: 720 };
            const targetFramerate = 30;

            // Detect supported encoder codecs — use target resolution to avoid
            // false negatives on mobile (e.g. Android HEVC encoders may not support 1080p)
            const supportedCodecs = await detectSupportedCodecs(targetSize.width, targetSize.height);
            this.supportedCodecs = supportedCodecs;

            // Cache supported encoder categories for later codec negotiation
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);
            infoLog?.log(`Supported encoder categories: [${this.supportedEncoderCategories.join(', ')}]`);

            // Pick initial codec: if audience codecs are known, pick the best encoder
            // codec that the audience can decode — avoids mid-stream codec switches.
            // For simulcast, also probe the candidate codec for N-concurrent-encoder
            // feasibility along the [av1, hevc, vp9, h264] priority chain — first
            // PASS wins. Avoids picking AV1/HEVC for sessions whose HW can encode a
            // single AV1 stream but not 3 in parallel (common on mid-range mobile).
            let bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const candidateLadder = this.simulcastLayers && this.simulcastLayers.length >= 2
                ? this.simulcastLayers
                : null;
            if (candidateLadder) {
                const simulcastPick = await this.pickSimulcastCodec(
                    supportedCodecs, audienceCodecs, candidateLadder);
                if (simulcastPick && simulcastPick !== bestCodecString) {
                    infoLog?.log(`Simulcast probe override: ${bestCodecString} → ${simulcastPick}`);
                    bestCodecString = simulcastPick;
                }
            }
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);
            const targetBitrate = getExpectedBitrate(bestCodecString, targetSize.height);
            infoLog?.log(`Initial codec selection: ${codecCategory} (${bestCodecString}), hw=${bestCodecInfo?.hardwareAccelerated ?? false}, bitrate=${targetBitrate / 1_000_000}Mbps`);

            // Unified ladder: even in single-encoder (P2P / solo) mode we build a
            // one-tier ladder so the same camera-capture-and-clamp path in
            // recording-service handles both cases. The C#-pushed simulcast
            // layers (top-first) become the multi-tier base; if absent, the
            // ladder collapses to a single top tier.
            //
            // HW-gated 1080p promotion: probe the selected codec for N+1
            // concurrent encoders (N = current top tier count, +1 for the new
            // 1080p tier; a single-encoder P2P probe is 1×1080p). Ladder is
            // bottom-first (index 0 = base, last = top). On pass, append
            // 1920x1080 as the new top. On fail, keep 720p cap.
            // recording-service re-clamps the ladder against the camera's
            // actual output so an unrealized 1080p tier gets dropped without
            // upscaling.
            let ladder: SpatialLayerConfig[] = this.simulcastLayers && this.simulcastLayers.length >= 2
                ? [...this.simulcastLayers]
                : [{ width: targetSize.width, height: targetSize.height, bitrate: targetBitrate }];
            const isSimulcast = ladder.length >= 2;
            const topDim = ladder[ladder.length - 1];
            if (topDim.width < 1920 && topDim.height < 1080) {
                const tier1080: SpatialLayerConfig = {
                    width: 1920,
                    height: 1080,
                    bitrate: getExpectedBitrate(bestCodecString, 1080),
                };
                // Simulcast: append 1080p as a new TOP on top of the existing
                // ladder (probe for N+1 concurrent encoders). Single-encoder:
                // REPLACE the existing single tier with 1080p (probe 1 encoder
                // only) — otherwise the downscaler would do 1080p→720p on
                // every frame despite the HW being perfectly capable of
                // encoding native 1080p directly, burning GPU time and
                // triggering backpressure step-down.
                const promoted: SpatialLayerConfig[] = isSimulcast
                    ? [...ladder, tier1080]
                    : [tier1080];
                // Budget: a single 1080p encoder needs more per-frame headroom
                // than the per-layer cost of concurrent simulcast extras
                // (lower layers finish fast and lift the median). 24ms leaves
                // iGPU HW a realistic chance at real-time single-encoder 1080p
                // (~25% headroom under the 33ms/30fps frame budget); 12ms is
                // right for multi-encoder concurrency gates. Bumped from 18 to
                // 24 in Bug O — peer B's HW-capable iGPU was rejected at 20ms.
                const budgetMs = isSimulcast ? 12 : 24;
                const probe = await probeConcurrentEncoders(bestCodecString, promoted, 8, budgetMs);
                if (probe.supported) {
                    infoLog?.log(`1080p promotion accepted: probe median=${probe.medianEncodeMs.toFixed(1)}ms (layers=${promoted.length}, budget=${budgetMs}ms)`);
                    ladder = promoted;
                } else {
                    infoLog?.log(`1080p promotion declined: probe median=${probe.medianEncodeMs.toFixed(1)}ms (stage=${probe.failedStage ?? 'timing'}, layers=${promoted.length}, budget=${budgetMs}ms)`);
                }
            }
            const base = ladder[0];
            const top = ladder[ladder.length - 1];
            const captureWidth = top.width;
            const captureHeight = top.height;
            const captureBitrate = top.bitrate;
            const simulcastLadder: SpatialLayerConfig[] = ladder;
            // Persist the promoted ladder so a subsequent C# setSimulcastLayers
            // call (e.g. SyncRemoteStreamCount activating simulcast) doesn't
            // clobber our locally-promoted ladder with a stale C# default.
            // Compared against in setSimulcastLayers below — we keep ours when
            // the incoming ladder is a strict subset (shorter / no top tier).
            // Bug N from the plan.
            this.simulcastLayers = simulcastLadder.length >= 2 ? [...simulcastLadder] : null;
            infoLog?.log(`Capture ladder (pre-clamp, bottom-first): [${ladder.map(l => `${l.width}x${l.height}`).join(', ')}], capture ${captureWidth}x${captureHeight}, primary ${base.width}x${base.height}`);

            // Create recording service with streaming config (uses video-pipeline internally)
            const config: RecordingConfig = {
                mode: 'webcam',
                codec: codecCategory,
                codecString: bestCodecString,
                hardwareAccelerated: bestCodecInfo?.hardwareAccelerated ?? false,
                scalabilityModes: bestCodecInfo?.scalabilityModes,
                width: captureWidth,
                height: captureHeight,
                bitrate: captureBitrate,
                framerate: targetFramerate,
                cameraDeviceId: this.selectedCameraDeviceId ?? undefined,
                backgroundBlur: {
                    enabled: this.isBlurEnabled,
                },
                // Enable streaming to server for real-time viewing
                streaming: {
                    enabled: true,
                    chatId: this.chatId,
                },
                // Enable VAD-based adaptive framerate to reduce bandwidth when not speaking
                adaptiveFramerate: {
                    enabled: true,
                },
                simulcastLadder,
            };

            this.recordingService = this.createRecordingService(config);

            // Start recording (this initializes the video-pipeline)
            await this.recordingService.start();

            this.previewTrack = this.recordingService.getInputTrack();
            // If the camera track ends externally (permission revoked, camera
            // unplugged, OS stole the device), fall back to full stop — same
            // pattern as screencast's browser-initiated "Stop sharing" below.
            // cleanupPreviewTrack clears this handler before calling stop() on
            // our own teardown to avoid re-entering stopRecording.
            if (this.previewTrack) {
                this.previewTrack.onended = () => {
                    infoLog?.log('Camera track ended externally — stopping recording');
                    void this.stopRecording();
                };
            }
            // Store actual camera resolution for capping reconfigure requests
            const trackSettings = this.previewTrack!.getSettings();
            infoLog?.log(`Track resolution: ${trackSettings.width}x${trackSettings.height}, facingMode=${trackSettings.facingMode ?? '(none)'}`);
            this.cameraWidth = trackSettings.width ?? config.width;
            this.cameraHeight = trackSettings.height ?? config.height;
            infoLog?.log(`Camera resolution: ${this.cameraWidth}x${this.cameraHeight}`);

            // Let Blazor resolve per-camera display prefs (mirror) from current
            // deviceId + facingMode. Fire-and-forget — purely cosmetic.
            void this.blazorRef.invokeMethodAsync(
                'OnTrackSettings',
                trackSettings.deviceId ?? null,
                trackSettings.facingMode ?? null);

            // Subscribe to VAD for adaptive framerate
            this.recordingService.getPipeline()?.subscribeToVad();

            // Fan out blur preview frames to every subscriber. Each listener draws
            // synchronously; the pipeline closes the frame immediately after this
            // callback returns (see `video-pipeline.ts`).
            this.recordingService.setPreviewCallback((frame: VideoFrame) => {
                if (!this.isBlurEnabled) return;
                for (const listener of this.previewFrameListeners) {
                    try {
                        listener(frame);
                    } catch (e) {
                        warnLog?.log('preview frame listener threw', e);
                    }
                }
            });

            this.isRecording = true;
            this.setRecordingState('recording');
            // Notify Blazor that recording started successfully
            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');

            infoLog?.log('Video recording started');
        } catch (error) {
            this.setRecordingState('error');
            errorLog?.log('Failed to start recording:', error);
            const message = error instanceof Error ? error.message : String(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        }
    }

    /**
     * Start screencast (screen sharing) recording
     */
    public async startScreencast(chatId: string, audienceCodecs?: string[]): Promise<void> {
        this.chatId = chatId;
        this.audienceCodecs = audienceCodecs;
        if (this.isRecording) {
            warnLog?.log('Already recording');
            return;
        }

        this.setRecordingState('starting');
        infoLog?.log('Starting screencast...');

        try {
            // Detect supported encoder codecs — use mobile-aware resolution to avoid
            // false negatives (e.g. Android HEVC encoders may not support 1080p)
            const detectionWidth = DeviceInfo.isMobile ? 1280 : 1920;
            const detectionHeight = DeviceInfo.isMobile ? 720 : 1080;
            const supportedCodecs = await detectSupportedCodecs(detectionWidth, detectionHeight);
            this.supportedEncoderCategories = this.extractEncoderCategories(supportedCodecs);

            // Pick initial codec based on audience
            const targetSize = { width: 1920, height: 1080 };
            const bestCodecString = this.pickInitialCodec(supportedCodecs, audienceCodecs, targetSize);
            const bestCodecInfo = supportedCodecs.find(c => c.codec === bestCodecString);
            const codecCategory = getCodecCategory(bestCodecString);

            // Screencast config: start at 1080p cap, quality preset will adjust.
            // No adaptiveFramerate — screencast has no voice-activity semantics;
            // cutting bitrate to 25% during silence would destroy text fidelity.
            // Framerate 15 matches the getDisplayMedia cap in captureScreencast
            // and gives the encoder ~2x bits-per-frame vs 30 fps at same bitrate.
            const config: RecordingConfig = {
                mode: 'screen',
                codec: codecCategory,
                codecString: bestCodecString,
                hardwareAccelerated: bestCodecInfo?.hardwareAccelerated ?? false,
                scalabilityModes: bestCodecInfo?.scalabilityModes,
                width: targetSize.width,
                height: targetSize.height,
                bitrate: getExpectedBitrate(bestCodecString, targetSize.height, 'screen'),
                framerate: 15,
                backgroundBlur: { enabled: false },
                streaming: {
                    enabled: true,
                    chatId: this.chatId,
                },
            };

            this.recordingService = this.createRecordingService(config);

            // Start recording — getDisplayMedia will prompt the user to pick a screen
            await this.recordingService.start();

            // Get the screen track for preview and track-ended detection
            const screenTrack = this.recordingService.getInputTrack();
            if (screenTrack) {
                // Use screen track for local preview
                this.previewTrack = screenTrack;

                // Store actual screen resolution for capping reconfigure requests
                const trackSettings = screenTrack.getSettings();
                this.cameraWidth = trackSettings.width ?? targetSize.width;
                this.cameraHeight = trackSettings.height ?? targetSize.height;
                infoLog?.log(`Screen resolution: ${this.cameraWidth}x${this.cameraHeight}`);

                // Handle browser's native "Stop sharing" button
                screenTrack.onended = () => {
                    infoLog?.log('Screen sharing track ended (user stopped sharing)');
                    void this.stopRecording();
                };
            }

            // Screencast does not subscribe to VAD — bitrate must stay full for text readability.

            this.isRecording = true;
            this.isScreencasting = true;
            this.setRecordingState('recording');

            await this.blazorRef.invokeMethodAsync('OnRecordingStarted');
            infoLog?.log('Screencast started');
        } catch (error) {
            // User-cancelled the picker (or denied permission). This is not an
            // error to surface — treat it as a graceful stop so the C# side
            // clears the screencast intent and the toggle flips back off.
            const isUserCancel = error instanceof DOMException && error.name === 'NotAllowedError';
            if (isUserCancel) {
                this.setRecordingState('stopped');
                infoLog?.log('Screencast cancelled by user');
                await this.blazorRef.invokeMethodAsync('OnRecordingStopped');
                return;
            }
            this.setRecordingState('error');
            errorLog?.log('Failed to start screencast:', error);
            const message = error instanceof Error ? error.message : String(error);
            await this.blazorRef.invokeMethodAsync('OnRecordingError', message);
        }
    }

    /**
     * Stop video recording
     */
    public async stopRecording(): Promise<void> {
        if (!this.isRecording || !this.recordingService) {
            return;
        }

        infoLog?.log('Stopping video recording...');

        try {
            await this.recordingService.stop();
            this.recordingService = null;
            this.cleanupPreviewTrack();
            this.isRecording = false;
            this.isScreencasting = false;
            this.setRecordingState('stopped');
            this.unregister();
            // Notify Blazor
            await this.blazorRef.invokeMethodAsync('OnRecordingStopped');

            infoLog?.log('Video recording stopped');
        } catch (error) {
            errorLog?.log('Failed to stop recording:', error);
        }
    }

    /**
     * Update the list of decoder codecs supported by all receivers.
     * The sender picks the best codec it can actually encode from this list.
     * Called from Blazor when the server pushes updated decoder capabilities.
     */
    public async updateSupportedDecoderCodecs(codecs: string[]): Promise<void> {
        if (!this.recordingService) return;

        // Filter server's list by sender's encoder capabilities
        const matchingCategories = codecs.filter(c => this.supportedEncoderCategories.includes(c));

        if (matchingCategories.length === 0) {
            warnLog?.log(`updateSupportedDecoderCodecs: no match between server codecs [${codecs.join(', ')}] and encoder capabilities [${this.supportedEncoderCategories.join(', ')}], keeping current codec`);
            return;
        }

        // Use getDefaultCodec() for HW-aware selection (same logic as initial codec pick)
        const audienceFilteredCodecs = this.supportedCodecs.filter(c =>
            c.supported && matchingCategories.includes(c.category)
        );
        if (audienceFilteredCodecs.length === 0) return;

        const pickedCodecString = getDefaultCodec(audienceFilteredCodecs, this.cameraWidth || 1280, this.cameraHeight || 720);
        const pickedCategory = getCodecCategory(pickedCodecString);

        infoLog?.log(`Selected encoder codec: ${pickedCategory} from supported decoders: [${codecs.join(', ')}]`);
        await this.recordingService.switchCodec(pickedCategory);
    }

    /**
     * Reconfigure encoder resolution (called from Blazor quality subscription).
     * Bitrate is derived from the current codec + capped height via bitrate-table.
     */
    public reconfigure(level: string, width: number, height: number): void {
        if (!this.recordingService) {
            warnLog?.log('reconfigure: no active recording service');
            return;
        }

        const pipeline = this.recordingService.getPipeline();
        if (!pipeline) return;

        // Handle server-driven pause
        if (level === 'Paused') {
            infoLog?.log('reconfigure: server paused this stream');
            pipeline.pauseEncoding();
            return;
        }

        // Resume if we were paused
        pipeline.resumeEncoding();

        // Transpose preset if camera orientation doesn't match (e.g., portrait camera, landscape preset)
        infoLog?.log(`reconfigure: level=${level}, size=${width}x${height}, cameraSize=${this.cameraWidth}x${this.cameraHeight}`);
        const cameraIsPortrait = this.cameraWidth > 0 && this.cameraHeight > 0 && this.cameraHeight > this.cameraWidth;
        const presetIsLandscape = width > height;
        if (cameraIsPortrait && presetIsLandscape)
            [width, height] = [height, width];

        // Cap to actual camera resolution — upscaling wastes CPU for no quality gain
        const cappedWidth = this.cameraWidth > 0 ? Math.min(width, this.cameraWidth) : width;
        const cappedHeight = this.cameraHeight > 0 ? Math.min(height, this.cameraHeight) : height;

        // Pick bitrate from the codec-aware table at the (possibly capped) height,
        // passing the current recording mode so screencast gets the higher-entropy
        // bitrate budget. Device caps for low-power hardware apply after.
        const currentCodec = this.recordingService.getConfig().codecString ?? '';
        const mode = this.recordingService.getConfig().mode;
        let cappedBitrate = getExpectedBitrate(currentCodec, cappedHeight, mode);
        if (DeviceInfo.isIos)
            cappedBitrate = Math.min(cappedBitrate, 1_000_000);
        else if (DeviceInfo.isMobile)
            cappedBitrate = Math.min(cappedBitrate, 2_000_000);

        infoLog?.log(`reconfigure: ${cappedWidth}x${cappedHeight} @ ${cappedBitrate / 1_000_000}Mbps (codec=${currentCodec})`);
        void pipeline.reconfigure({ bitrate: cappedBitrate, width: cappedWidth, height: cappedHeight });
    }

    public forceKeyFrame(): void {
        const pipeline = this.recordingService?.getPipeline();
        if (!pipeline) {
            warnLog?.log('forceKeyFrame: no active pipeline');
            return;
        }
        infoLog?.log('forceKeyFrame: PLI — forcing keyframe on encoder');
        void pipeline.forceKeyFrame();
    }

    // Probes the [av1, hevc, vp9, h264] priority chain for simulcast feasibility
    // on the given ladder. Returns the first codec category whose probe passes,
    // mapped to a concrete codec string from `supportedCodecs`. Returns null
    // when no candidate passes — caller falls back to the static priority pick
    // and accepts the risk of backpressure-driven step-down.
    //
    // Filters by `audienceCodecs` (if provided) and `supportedEncoderCategories`
    // — same constraints as `pickFallbackCodec`. Probe budget 12ms matches the
    // simulcast concurrency gate used in 1080p promotion.
    private async pickSimulcastCodec(
        supportedCodecs: CodecInfo[],
        audienceCodecs: string[] | undefined,
        ladder: SpatialLayerConfig[],
    ): Promise<string | null> {
        const priority: ('av1' | 'hevc' | 'vp9' | 'h264')[] = ['av1', 'hevc', 'vp9', 'h264'];
        const audience = audienceCodecs && audienceCodecs.length > 0 ? audienceCodecs : null;
        for (const category of priority) {
            if (!this.supportedEncoderCategories.includes(category)) continue;
            if (audience && !audience.includes(category)) continue;
            const codecInfo = supportedCodecs.find(c => c.category === category && c.supported && c.hardwareAccelerated)
                ?? supportedCodecs.find(c => c.category === category && c.supported);
            if (!codecInfo) continue;
            const probe = await probeConcurrentEncoders(codecInfo.codec, ladder, 8, 12);
            if (probe.supported) {
                infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) PASS — median=${probe.medianEncodeMs.toFixed(1)}ms over ${ladder.length} layer(s)`);
                return codecInfo.codec;
            }
            infoLog?.log(`pickSimulcastCodec: ${category} (${codecInfo.codec}) FAIL — median=${probe.medianEncodeMs.toFixed(1)}ms, stage=${probe.failedStage ?? 'timing'}`);
        }
        return null;
    }

    private pickInitialCodec(supportedCodecs: CodecInfo[], audienceCodecs: string[] | undefined, size: Size) {
        if (audienceCodecs && audienceCodecs.length > 0) {
            const matchingCategories = audienceCodecs.filter(c => this.supportedEncoderCategories.includes(c));
            if (matchingCategories.length > 0) {
                const audienceFilteredCodecs = supportedCodecs.filter(c =>
                    c.supported && matchingCategories.includes(c.category),
                );
                return audienceFilteredCodecs.length > 0
                    ? getDefaultCodec(audienceFilteredCodecs, size.width, size.height)
                    : getDefaultCodec(supportedCodecs, size.width, size.height);
            } else {
                return getDefaultCodec(supportedCodecs, size.width, size.height);
            }
        } else {
            return getDefaultCodec(supportedCodecs, size.width, size.height);
        }
    }

    private createRecordingService(config: RecordingConfig): RecordingService {
        const recordingService = new RecordingService(config);
        recordingService.addEventListener('state-change', ((event: CustomEvent<RecordingState>) => {
            this.onRecorderStateChange(event.detail);
        }) as EventListener);
        recordingService.addEventListener('error', ((event: CustomEvent<Error>) => {
            this.onRecorderError(event.detail);
        }) as EventListener);
        recordingService.addEventListener('encoder-failure', ((event: CustomEvent<string>) => {
            this.onEncoderCodecFailed(event.detail);
        }) as EventListener);
        return recordingService;
    }

    /** Remove failed encoder codec and switch to the next one in priority order. */
    private onEncoderCodecFailed(category: string): void {
        const idx = this.supportedEncoderCategories.indexOf(category);
        if (idx >= 0) {
            this.supportedEncoderCategories.splice(idx, 1);
            warnLog?.log(`Excluded encoder codec '${category}' after failure. Remaining: [${this.supportedEncoderCategories.join(', ')}]`);
        }
        const next = this.pickFallbackCodec(category);
        if (!next) {
            errorLog?.log(`No fallback codec available after '${category}' failed`);
            return;
        }
        warnLog?.log(`Switching codec '${category}' → '${next}' after failure`);
        void this.recordingService?.switchCodec(next);
    }

    /**
     * Pick the next codec to try after `failedCategory`. Walks a fixed priority
     * chain (AV1 → HEVC → VP9 → H.264) and returns the first category that is
     *   - still supported by this HW encoder (`supportedEncoderCategories`)
     *   - decodable by every known audience peer (if audience codecs known)
     *   - NOT the one that just failed
     * H.264 is the universal floor — returned last. Returns null if the
     * priority chain is exhausted.
     */
    private pickFallbackCodec(failedCategory: string): 'av1' | 'hevc' | 'vp9' | 'h264' | null {
        const priority: ('av1' | 'hevc' | 'vp9' | 'h264')[] = ['av1', 'hevc', 'vp9', 'h264'];
        const audience = this.audienceCodecs;
        for (const category of priority) {
            if (category === failedCategory) continue;
            if (!this.supportedEncoderCategories.includes(category)) continue;
            if (audience && audience.length > 0 && !audience.includes(category)) continue;
            return category;
        }
        return null;
    }

    private register(kind: number): void {
        this.registeredKind = kind;
        activeRecorders.set(kind, this);
        notifyRegistryListeners(this, kind);
    }

    private unregister(): void {
        const kind = this.registeredKind;
        if (kind !== null && activeRecorders.get(kind) === this) {
            activeRecorders.delete(kind);
            notifyRegistryListeners(null, kind);
        }
        this.registeredKind = null;
    }

    private setRecordingState(next: VideoRecordingState): void {
        if (this._recordingState === next) return;
        this._recordingState = next;
        for (const cb of this.stateChangeListeners) {
            try { cb(next); } catch (e) { warnLog?.log('state change listener threw', e); }
        }
    }

    private setIsBlurEnabled(next: boolean): void {
        if (this.isBlurEnabled === next) return;
        this.isBlurEnabled = next;
        for (const cb of this.blurChangeListeners) {
            try { cb(next); } catch (e) { warnLog?.log('blur change listener threw', e); }
        }
    }

    private cleanupPreviewTrack(): void {
        if (this.previewTrack) {
            // Detach our onended handler first so our own track.stop() below
            // doesn't re-enter stopRecording via the external-death callback.
            this.previewTrack.onended = null;
            // For screencast, don't stop the track — it's shared with the pipeline.
            // The pipeline's stop() will handle track cleanup.
            if (!this.isScreencasting)
                this.previewTrack.stop();
            this.previewTrack = null;
        }
    }

    /**
     * Handle recorder state changes
     */
    private onRecorderStateChange(state: RecordingState): void {
        // Skip pure duration ticks — only act on actual status transitions
        if (state.status === this.lastStatus)
            return;
        this.lastStatus = state.status;

        debugLog?.log('Recorder state changed:', state);
    }

    /**
     * Handle recorder errors
     */
    private onRecorderError(error: Error): void {
        errorLog?.log('Recorder error:', error);
        void this.blazorRef.invokeMethodAsync('OnRecordingError', error.message);
    }

    /**
     * Extract unique encoder codec categories from detected codec support.
     * Returns categories like ['av1', 'h264'] based on what the encoder can actually produce.
     */
    private extractEncoderCategories(codecs: CodecInfo[]): string[] {
        const categories = new Set<string>();
        for (const c of codecs) {
            if (c.supported) {
                // AV1 software encoding is too expensive for real-time — require HW
                if (c.category === 'av1' && !c.hardwareAccelerated) continue;
                // On mobile, SW encoding is too CPU-intensive for anything except H264
                // (VP9-SW on Android silently drops all frames, HEVC-SW is equally broken)
                if (DeviceInfo.isMobile && !c.hardwareAccelerated && c.category !== 'h264') continue;
                categories.add(c.category);
            }
        }
        // Return in priority order: av1, hevc, vp9, h264
        const ordered: string[] = [];
        if (categories.has('av1')) ordered.push('av1');
        if (categories.has('hevc')) ordered.push('hevc');
        if (categories.has('vp9')) ordered.push('vp9');
        if (categories.has('h264')) ordered.push('h264');
        return ordered;
    }

    public getDiagnostics(): OwnStreamDiagnostics {
        const rs = this.recordingService;
        const pipeline = rs?.getPipeline();
        const encoderStats = pipeline?.getEncoderStats();
        const segStats = pipeline?.getSegmentationStats();
        const orientStats = pipeline?.getOrientationStats();
        const state = rs?.getState();
        const config = rs?.getConfig();
        const inputTrack = rs?.getInputTrack();
        const trackSettings = inputTrack?.getSettings();

        const duration = state?.duration ?? 0;
        const actualBitrateKbps = duration > 0 && encoderStats
            ? (encoderStats.totalBytes * 8) / duration / 1000
            : 0;

        return {
            mode: this.isScreencasting ? 'screen' : this.isRecording ? 'webcam' : 'none',
            codec: config?.codecString ?? '',
            codecCategory: config?.codecString ? getCodecCategory(config.codecString) : '',
            hardwareAccelerated: config?.hardwareAccelerated ?? false,
            inputResolution: trackSettings ? `${trackSettings.width ?? 0}x${trackSettings.height ?? 0}` : 'N/A',
            inputFramerate: trackSettings?.frameRate ?? 0,
            outputResolution: encoderStats
                ? `${encoderStats.configuredWidth}x${encoderStats.configuredHeight}`
                : 'N/A',
            configuredBitrate: encoderStats?.configuredBitrate ?? 0,
            actualBitrateKbps: Math.round(actualBitrateKbps),
            encodedFrames: encoderStats?.encodedFrames ?? 0,
            droppedFrames: encoderStats?.droppedFrames ?? 0,
            keyFrames: encoderStats?.keyFrames ?? 0,
            medianEncodeTime: encoderStats?.medianEncodeTime ?? 0,
            pureMedianEncodeTime: encoderStats?.pureMedianEncodeTime ?? 0,
            encoderHwAccel: encoderStats?.hardwareAcceleration ?? 'unknown',
            duration,
            cameraLabel: inputTrack?.label ?? null,
            blurEnabled: this.isBlurEnabled,
            segmentationBackend: segStats?.backend ?? null,
            segmentationAvgTime: segStats?.averageTotalTime ?? null,
            supportedEncoderCategories: this.supportedEncoderCategories,
            status: state?.status ?? 'idle',
            orientation: orientStats ? {
                firstDisplayResolution: `${orientStats.firstDisplayWidth}x${orientStats.firstDisplayHeight}`,
                firstCodedResolution: `${orientStats.firstCodedWidth}x${orientStats.firstCodedHeight}`,
                firstRotation: orientStats.firstRotation !== null ? `${orientStats.firstRotation}°` : 'N/A',
                lastRotation: orientStats.lastRotation !== null ? `${orientStats.lastRotation}°` : 'N/A',
                configuredResolution: `${orientStats.configuredWidth}x${orientStats.configuredHeight}`,
                needsRotation: orientStats.needsRotation,
                rotationDetection: orientStats.rotationDetection,
                framesSeen: orientStats.framesSeen,
            } : null,
        };
    }

    public dispose() {
        if (this.disposed)
            return;
        this.disposed = true;
        this.unregister();

        // Drop listeners before tearing down the pipeline so no in-flight
        // preview callback reaches a listener after we're gone.
        this.previewFrameListeners.clear();

        this.cleanupPreviewTrack();

        // Stop recording service
        if (this.recordingService) {
            void this.recordingService.stop();
            this.recordingService = null;
        }

        this.isRecording = false;
        this.isScreencasting = false;
        this.setRecordingState('stopped');
    }
}
