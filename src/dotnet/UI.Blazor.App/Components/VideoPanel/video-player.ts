import * as signalR from '@microsoft/signalr';
import { decode } from '@msgpack/msgpack';
import { Log } from 'logging';
import { fastRaf } from 'fast-raf';
import { ServerClock } from 'server-clock';
import { rpcClientServer } from 'rpc';
import type { Disposable } from 'disposable';
import { VideoStreamer } from '../../Services/Video/video-streamer';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';
import { AudioVideoSync } from 'audio-video-sync';
import { DocumentEvents } from 'event-handling';
import { Versioning } from 'versioning';
import { type Subscription } from 'rxjs';
import type { DecoderWorker } from '../../Services/Video/workers/decoder-worker-contract';
import type { DecoderConfig } from '../../Services/Video/webcodecs-decoder';

const { debugLog, warnLog, errorLog } = Log.get('VideoPlayer');

function arrayBufferEqual(a: AllowSharedBufferSource, b: AllowSharedBufferSource): boolean {
    const viewA = ArrayBuffer.isView(a) ? new Uint8Array(a.buffer, a.byteOffset, a.byteLength) : new Uint8Array(a);
    const viewB = ArrayBuffer.isView(b) ? new Uint8Array(b.buffer, b.byteOffset, b.byteLength) : new Uint8Array(b);
    if (viewA.length !== viewB.length) return false;
    for (let i = 0; i < viewA.length; i++) {
        if (viewA[i] !== viewB[i]) return false;
    }
    return true;
}

export class VideoPlayer {
    private blazorRef: DotNet.DotNetObject;
    private streamId: string;
    private authorId: string;
    private canvas: HTMLCanvasElement;
    private canvasCtx: CanvasRenderingContext2D | null = null;

    // Decoder worker (off-main-thread decoding)
    private decoderWorkerInstance: Worker | null = null;
    private decoderWorker: (DecoderWorker & Disposable) | null = null;
    private decoderConfig: DecoderConfig | null = null;
    private pendingFrames: VideoFrame[] = [];
    private isPlaying = false;
    private visibilitySubscription: Subscription | null = null;

    // Buffer chunks until we receive a keyframe with description
    private waitingForKeyframe = true;
    private lastDescription: ArrayBuffer | null = null;

    // Buffering state
    private bufferSize = 0;
    private readonly maxBufferSize = 5; // frames
    private lastReportedBufferLow = true;

    // SignalR pull subscription
    private pullSubscription: signalR.ISubscription<Uint8Array> | null = null;

    // Frame pacing state
    private playbackStartTime = 0;     // wall-clock ms (performance.now) when first frame rendered
    private firstFrameTimestamp = 0;    // timestamp of first decoded frame (microseconds)
    private renderKey: string;
    private renderFrameCount = 0;       // count of rendered frames (for periodic logging)
    private receivedFrameCount = 0;     // count of received frames (for periodic logging)
    private receivedKeyframeCount = 0;   // count of received keyframes (for correlation with encoder)
    private lastSyncLogTime = 0;        // throttle sync logging
    private sequenceNumber = 0;         // sequence number for chunks sent to decoder worker

    // Diagnostics counters for 10s delta reporting
    private lastDiagDecodedFrames = 0;
    private lastDiagReceivedFrames = 0;

    // Latency measurement
    private lastRenderedOffsetMs = 0;   // offset of the latest decoded frame (ms from stream start)
    private latencyReportTimer: ReturnType<typeof setInterval> | null = null;
    private pipelineLatencyMs = 0;      // Smoothed video pipeline latency estimate (ms)
    private skipFramesBelowOffsetMs = 0; // After tab restore, skip decoded frames below this offset
    private skippedBacklogFrames = 0;
    private rebufferDelayMs = 0;         // After tab restore, delay rendering to let buffer accumulate

    // Audio sync
    private startedAtMs: number;

    /** Creates a new VideoPlayer instance for Blazor interop */
    static create(
        canvas: HTMLCanvasElement,
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        startedAtMs: number
    ): VideoPlayer {
        return new VideoPlayer(blazorRef, streamId, authorId, codec, width, height, codecSettings, canvas, startedAtMs);
    }

    constructor(
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        canvas: HTMLCanvasElement,
        startedAtMs: number
    ) {
        this.blazorRef = blazorRef;
        this.streamId = streamId;
        this.authorId = authorId;
        this.startedAtMs = startedAtMs;
        this.canvas = canvas;
        this.canvasCtx = canvas.getContext('2d');
        this.renderKey = `vr-${streamId}`;

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(
            `VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}, ` +
            `authorId=${authorId}, startedAtMs=${startedAtMs.toFixed(0)}`);

        // Initialize decoder worker
        void this.initDecoderWorker(codec, width, height, codecSettings);
    }

    private async initDecoderWorker(codec: string, width: number, height: number, codecSettings: string): Promise<void> {
        if (!this.supportsWebCodecs()) {
            warnLog?.log('WebCodecs not supported');
            return;
        }

        try {
            // Decode codec settings (base64 encoded SPS/PPS for H.264)
            let description: ArrayBuffer | undefined;
            if (codecSettings) {
                const binaryString = atob(codecSettings);
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {
                    bytes[i] = binaryString.charCodeAt(i);
                }
                description = bytes.buffer;
                debugLog?.log(`Decoded description: ${bytes.length} bytes`);
            }

            // Map codec name to WebCodecs codec string
            const codecString = this.mapCodecToWebCodecs(codec, description);
            debugLog?.log(`Initializing decoder worker with codec: ${codecString}`);

            this.decoderConfig = {
                codec: codecString,
                optimizeForLatency: true,
                hardwareAcceleration: 'prefer-hardware',
                description,
            };

            // Create decoder worker
            const decoderWorkerPath = Versioning.mapPath('/dist/videoDecoderWorker.js');
            this.decoderWorkerInstance = new Worker(decoderWorkerPath, { type: 'module' });
            this.decoderWorkerInstance.onerror = (e) => errorLog?.log('Decoder worker error:', e);

            // Create RPC proxy — decoded frames are transferred back from worker
            this.decoderWorker = rpcClientServer<DecoderWorker>(
                'VideoPlayer.decoder',
                this.decoderWorkerInstance,
                { onDecodedFrame: (frame: VideoFrame) => { this.onFrameDecoded(frame); return Promise.resolve(); } }
            );

            // Initialize the worker
            await this.decoderWorker.initialize(this.decoderConfig, { type: 'rpc-timeout', timeoutMs: 5000 });
            debugLog?.log('Decoder worker initialized');

            // If we have codec settings or this is AV1, we don't need to wait for keyframe with description
            const isAV1 = codecString.startsWith('av01');
            if (codecSettings || isAV1) {
                this.waitingForKeyframe = false;
                debugLog?.log(`Not waiting for keyframe with description (codecSettings=${!!codecSettings}, isAV1=${isAV1})`);
            }
        } catch (error) {
            errorLog?.log('Failed to initialize decoder worker:', error);
        }
    }

    private supportsWebCodecs(): boolean {
        return typeof VideoDecoder !== 'undefined';
    }

    private mapCodecToWebCodecs(codec: string, description?: ArrayBuffer): string {
        // Defense-in-depth: detect avcC format from description bytes regardless of declared codec
        if (description && description.byteLength >= 5) {
            if (VideoPlayer.isAvcCDescription(description)) {
                const bytes = new Uint8Array(description);
                const profileIndication = bytes[1];
                const profileCompatibility = bytes[2];
                const levelIndication = bytes[3];
                const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
                const declaredLower = codec.toLowerCase();
                if (declaredLower !== 'h264' && declaredLower !== 'avc1' && !declaredLower.startsWith('avc1.')) {
                    warnLog?.log(`Codec mismatch: declared=${codec} but description is avcC, overriding to ${codecString}`);
                }
                return codecString;
            }
        }

        // If we have an avcC description and declared H.264, extract the actual profile
        if (description && description.byteLength >= 4 && (codec.toLowerCase() === 'h264' || codec.toLowerCase() === 'avc1')) {
            const bytes = new Uint8Array(description);
            const profileIndication = bytes[1];
            const profileCompatibility = bytes[2];
            const levelIndication = bytes[3];
            const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
            debugLog?.log(`Built codec string from avcC: ${codecString}`);
            return codecString;
        }

        // Map common codec names to WebCodecs codec strings
        const codecMap: Record<string, string> = {
            'h264': 'avc1.640028',
            'avc1': 'avc1.640028',
            'h265': 'hvc1.1.6.L93.90',
            'hevc': 'hvc1.1.6.L93.90',
            'vp8': 'vp8',
            'vp9': 'vp09.00.10.08',
            'av1': 'av01.0.01M.08',
        };

        const lowerCodec = codec.toLowerCase();
        if (codecMap[lowerCodec]) {
            return codecMap[lowerCodec];
        }
        if (codec.includes('.')) {
            return codec;
        }
        return 'avc1.640028';
    }

    private static isAvcCDescription(description: ArrayBuffer): boolean {
        if (description.byteLength < 5) return false;
        const bytes = new Uint8Array(description);
        if (bytes[0] !== 0x01) return false;
        const validProfiles = [66, 77, 88, 100, 110, 122, 244];
        if (!validProfiles.includes(bytes[1])) return false;
        if ((bytes[4] & 0xFC) !== 0xFC) return false;
        return true;
    }

    private onFrameDecoded(frame: VideoFrame): void {
        // After tab restore, skip old frames from decoder's internal backlog
        if (this.skipFramesBelowOffsetMs > 0) {
            const frameOffsetMs = frame.timestamp / 1000; // μs → ms
            if (frameOffsetMs < this.skipFramesBelowOffsetMs) {
                frame.close();
                this.skippedBacklogFrames++;
                if (this.skippedBacklogFrames <= 3 || this.skippedBacklogFrames % 10 === 0) {
                    debugLog?.log(
                        `Skipping backlog frame #${this.skippedBacklogFrames}: ` +
                        `frameOffset=${frameOffsetMs.toFixed(0)}ms, threshold=${this.skipFramesBelowOffsetMs.toFixed(0)}ms`);
                }
                return;
            }
            // Caught up — resume normal rendering
            debugLog?.log(
                `Decoder backlog cleared: skipped ${this.skippedBacklogFrames} frames, ` +
                `resumed at offset ${frameOffsetMs.toFixed(0)}ms (threshold was ${this.skipFramesBelowOffsetMs.toFixed(0)}ms)`);
            this.skipFramesBelowOffsetMs = 0;
        }

        this.pendingFrames.push(frame);
        this.bufferSize++;

        // Update pipeline latency estimate from this fresh frame
        const frameOffsetMs = frame.timestamp / 1000; // μs → ms
        const capturedAtMs = this.startedAtMs + frameOffsetMs;
        const currentLatencyMs = ServerClock.now() - capturedAtMs;
        this.pipelineLatencyMs = this.pipelineLatencyMs === 0
            ? Math.max(currentLatencyMs, 0)
            : this.pipelineLatencyMs * 0.95 + currentLatencyMs * 0.05;

        // Safety cap: prevent unbounded buffer growth (30 frames = 1s at 30fps)
        while (this.pendingFrames.length > 30) {
            const dropped = this.pendingFrames.shift()!;
            dropped.close();
            this.bufferSize--;
        }
        this.scheduleRender();
    }

    private onDecoderError(error: Error): void {
        errorLog?.log('Decoder error:', error);
        void this.reportEnded(error.message);
    }

    private scheduleRender(): void {
        if (this.pendingFrames.length === 0 || !this.isPlaying) return;
        fastRaf({ write: () => this.onRenderFrame(), key: this.renderKey });
    }

    private onRenderFrame(): void {
        if (!this.isPlaying || this.pendingFrames.length === 0) return;

        const now = performance.now();

        // Initialize timing anchor on first frame
        if (this.playbackStartTime === 0) {
            this.playbackStartTime = now + this.rebufferDelayMs;
            this.rebufferDelayMs = 0;
            this.firstFrameTimestamp = this.pendingFrames[0].timestamp; // μs
        }

        this.renderFrameCount++;

        // Compute target — audio-driven when available, wall-clock fallback
        let targetTimestamp: number;
        const audioState = AudioVideoSync.get(this.authorId);
        if (audioState) {
            const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
            const rawTargetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs) + audioPlayingAtMs;
            const targetVideoOffsetMs = rawTargetVideoOffsetMs - this.pipelineLatencyMs;
            targetTimestamp = targetVideoOffsetMs * 1000;

            this.playbackStartTime = now;
            this.firstFrameTimestamp = targetTimestamp;

            if (now - this.lastSyncLogTime > 1000) {
                this.lastSyncLogTime = now;
                const driftMs = this.lastRenderedOffsetMs - targetVideoOffsetMs;
                debugLog?.log(
                    `audioSync: rawTargetMs=${rawTargetVideoOffsetMs.toFixed(0)}, ` +
                    `pipelineMs=${this.pipelineLatencyMs.toFixed(0)}, ` +
                    `targetMs=${targetVideoOffsetMs.toFixed(0)}, ` +
                    `renderedMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                    `driftMs=${driftMs.toFixed(0)}, pending=${this.pendingFrames.length}`);
            }
        } else {
            const elapsedUs = (now - this.playbackStartTime) * 1000;
            targetTimestamp = this.firstFrameTimestamp + elapsedUs - this.pipelineLatencyMs * 1000;

            if (now - this.lastSyncLogTime > 2000) {
                this.lastSyncLogTime = now;
                debugLog?.log(`audioSync: no audio state for authorId=${this.authorId}`);
            }
        }

        if (this.renderFrameCount % 60 === 0) {
            debugLog?.log(
                `onRenderFrame #${this.renderFrameCount}: lastRenderedOffsetMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                `pendingFrames=${this.pendingFrames.length}`);
        }

        // Find the latest frame due for presentation; drop earlier ones
        let frameToRender: VideoFrame | null = null;
        while (this.pendingFrames.length > 0 && this.pendingFrames[0].timestamp <= targetTimestamp) {
            if (frameToRender) {
                frameToRender.close();
                this.bufferSize--;
            }
            frameToRender = this.pendingFrames.shift()!;
        }

        if (frameToRender) {
            this.bufferSize--;
            this.lastRenderedOffsetMs = frameToRender.timestamp / 1000;
            this.drawFrame(frameToRender);
            frameToRender.close();
        }

        this.updateBufferState();

        if (this.pendingFrames.length > 0)
            this.scheduleRender();
    }

    private drawFrame(frame: VideoFrame): void {
        if (!this.canvasCtx) return;
        try {
            if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
                this.canvas.width = frame.displayWidth;
                this.canvas.height = frame.displayHeight;
                debugLog?.log(`Canvas resized to ${frame.displayWidth}x${frame.displayHeight}`);
            }
            this.canvasCtx.drawImage(frame, 0, 0);
        } catch (error) {
            errorLog?.log('Error rendering frame:', error);
        }
    }

    private updateBufferState(): void {
        const isBufferLow = this.bufferSize < 3;
        if (isBufferLow !== this.lastReportedBufferLow) {
            this.lastReportedBufferLow = isBufferLow;
            void this.reportPlaying(0, isBufferLow);
        }
    }

    public pushFrame(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array
    ): void {
        if (!this.isPlaying || !this.decoderWorker) {
            return;
        }

        // After tab restore: skip stale encoded frames arriving from SignalR
        if (this.skipFramesBelowOffsetMs > 0 && timestampMs < this.skipFramesBelowOffsetMs) {
            return;
        }

        // If we're waiting for a keyframe with description, buffer chunks
        if (this.waitingForKeyframe) {
            const needsDescription = !!this.decoderConfig?.description;
            if (isKeyFrame && (!needsDescription || (description && description.length > 0))) {
                // After tab restore: skip keyframes that are too old
                if (this.skipFramesBelowOffsetMs > 0 && timestampMs < this.skipFramesBelowOffsetMs) {
                    debugLog?.log(`Skipping old keyframe at offset ${timestampMs.toFixed(0)}ms ` +
                        `(threshold=${this.skipFramesBelowOffsetMs.toFixed(0)}ms)`);
                    return;
                }
                this.skipFramesBelowOffsetMs = 0;

                debugLog?.log(`Got keyframe: descLen=${description?.length ?? 0}, needsDesc=${needsDescription}`);
                this.waitingForKeyframe = false;

                // Reconfigure decoder worker with description if needed
                if (description && description.length > 0 && this.decoderConfig) {
                    const descBuffer = description.buffer.slice(
                        description.byteOffset,
                        description.byteOffset + description.byteLength
                    );
                    this.lastDescription = descBuffer as ArrayBuffer;

                    // Re-derive codec from description (defense-in-depth)
                    const derivedCodec = this.mapCodecToWebCodecs(
                        this.decoderConfig.codec, descBuffer as ArrayBuffer);

                    const newConfig: DecoderConfig = {
                        ...this.decoderConfig,
                        codec: derivedCodec,
                        description: descBuffer,
                    };
                    this.decoderConfig = newConfig;
                    void this.decoderWorker.configureDecoder(newConfig);
                }

                // Send keyframe to decoder worker
                this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description);
            }
            // Drop delta frames while waiting for keyframe
            return;
        }

        // If we receive a new keyframe with description, reconfigure only if changed
        if (isKeyFrame && description && description.length > 0) {
            const descBuffer = description.buffer.slice(
                description.byteOffset,
                description.byteOffset + description.byteLength
            );
            const descChanged = !this.lastDescription || !arrayBufferEqual(this.lastDescription, descBuffer);
            if (descChanged) {
                debugLog?.log(`Reconfiguring decoder worker with new description: ${description.length} bytes`);
                this.lastDescription = descBuffer as ArrayBuffer;

                if (this.decoderConfig) {
                    const derivedCodec = this.mapCodecToWebCodecs(
                        this.decoderConfig.codec, descBuffer as ArrayBuffer);
                    const newConfig: DecoderConfig = {
                        ...this.decoderConfig,
                        codec: derivedCodec,
                        description: descBuffer,
                    };
                    this.decoderConfig = newConfig;
                    void this.decoderWorker.configureDecoder(newConfig);
                }
                this.playbackStartTime = 0;
            }
        }

        this.sendToDecoderWorker(frameData, timestampMs, durationMs, isKeyFrame, description);
    }

    private sendToDecoderWorker(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean,
        description?: Uint8Array
    ): void {
        if (!this.decoderWorker) return;

        // Copy data to transferable ArrayBuffer
        const dataBuffer = new ArrayBuffer(frameData.byteLength);
        new Uint8Array(dataBuffer).set(frameData);

        let descBuffer: ArrayBuffer | undefined;
        if (description && description.length > 0) {
            descBuffer = new ArrayBuffer(description.byteLength);
            new Uint8Array(descBuffer).set(description);
        }

        // Send raw bytes to worker — worker creates EncodedVideoChunk internally
        void this.decoderWorker.decodeRawChunk(
            dataBuffer,
            timestampMs * 1000, // ms → μs
            durationMs * 1000,  // ms → μs
            isKeyFrame,
            this.sequenceNumber++,
            descBuffer
        );
    }

    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
        debugLog?.log(`VideoPlayer started for stream ${this.streamId}`);

        // Listen for tab visibility restore to avoid frame burst after backgrounding
        this.visibilitySubscription = DocumentEvents.passive.visibilityChange$.subscribe(() => {
            if (!document.hidden && this.isPlaying) {
                debugLog?.log('visibilityChange: tab became visible');
                this.onVisibilityRestored();
            }
        });

        // Report initial playing state
        void this.reportPlaying(0, true);
    }

    private onVisibilityRestored(): void {
        if (!this.decoderWorker) return;

        this.skippedBacklogFrames = 0;
        const pendingCount = this.pendingFrames.length;

        // Close all pending decoded frames to avoid burst playback
        for (const frame of this.pendingFrames) {
            try { frame.close(); } catch { /* already closed */ }
        }
        this.pendingFrames = [];
        this.bufferSize = 0;

        // Reset decoder worker to flush its internal queue
        void this.decoderWorker.resetDecoder();

        // After reset, delta frames are useless — need a fresh keyframe
        this.waitingForKeyframe = true;

        // Set threshold to skip stale encoded frames arriving from SignalR
        const targetLatencyMs = Math.max(this.pipelineLatencyMs, 300);
        this.skipFramesBelowOffsetMs = (ServerClock.now() - this.startedAtMs) - targetLatencyMs;

        warnLog?.log(
            `Tab restored: flushed ${pendingCount} pending frames, decoder reset, ` +
            `waiting for keyframe above offset ${this.skipFramesBelowOffsetMs.toFixed(0)}ms`);

        // Reset timing anchor so playback re-syncs on next rendered frame
        this.playbackStartTime = 0;
        this.rebufferDelayMs = 300;
    }

    /** Called by Blazor */
    public async startPull(streamId: string, skipToMs: number): Promise<void> {
        if (!this.isPlaying) {
            warnLog?.log('startPull called but player not started');
            return;
        }

        const hubUrl = new URL('/api/hub/streams', window.location.origin).toString();
        VideoStreamer.init(hubUrl);
        await VideoStreamer.ensureConnected();

        const connection = VideoStreamer.connection!;
        const sessionToken = SessionTokens.current;
        debugLog?.log(`startPull: stream=${streamId}, skipTo=${skipToMs}ms`);

        const streamResult = connection.stream<Uint8Array>(
            'GetVideo', sessionToken, streamId, skipToMs);

        // Start latency report timer now that we're receiving frames
        this.latencyReportTimer ??= setInterval(() => this.reportLatencyTick(), 10_000);

        this.pullSubscription = streamResult.subscribe({
            next: (frameBytes: Uint8Array) => {
                this.processReceivedFrame(frameBytes);
            },
            error: (err: Error) => {
                errorLog?.log('Pull stream error:', err);
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                void this.reportEnded(err?.message ?? 'Pull stream error');
            },
            complete: () => {
                debugLog?.log('Pull stream completed');
                void this.reportEnded(undefined);
            },
        });
    }

    private processReceivedFrame(frameBytes: Uint8Array): void {
        try {
            const map = decode(frameBytes) as Record<string, unknown>;

            const offset = map.offset as number;          // .NET ticks
            const duration = map.duration as number;       // .NET ticks
            const isKeyFrame = (map.isKeyFrame as boolean | undefined) ?? false;
            const data = map.data as Uint8Array;
            const description = map.description as Uint8Array | undefined;

            // Convert ticks to milliseconds (1 tick = 100ns = 0.0001ms)
            const offsetMs = offset / 10000;
            const durationMs = duration / 10000;

            this.receivedFrameCount++;
            if (isKeyFrame) {
                this.receivedKeyframeCount++;
                debugLog?.log(
                    `Received keyframe #${this.receivedKeyframeCount}: offsetMs=${offsetMs.toFixed(0)}, ` +
                    `dataLen=${data.length}, descLen=${description?.length ?? 0}`);
            } else if (this.receivedFrameCount % 100 === 1) {
                debugLog?.log(
                    `processReceivedFrame #${this.receivedFrameCount}: offsetMs=${offsetMs.toFixed(0)}, ` +
                    `durationMs=${durationMs.toFixed(1)}, dataLen=${data.length}`);
            }

            this.pushFrame(data, offsetMs, durationMs, isKeyFrame, description);
        } catch (error) {
            errorLog?.log('Error deserializing received frame:', error);
        }
    }

    public stopPull(): void {
        if (this.pullSubscription) {
            this.pullSubscription.dispose();
            this.pullSubscription = null;
        }
    }

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        this.isPlaying = false;
        this.playbackStartTime = 0;
        this.lastRenderedOffsetMs = 0;
        this.renderFrameCount = 0;
        this.receivedFrameCount = 0;
        this.receivedKeyframeCount = 0;
        this.pipelineLatencyMs = 0;
        this.skipFramesBelowOffsetMs = 0;
        this.skippedBacklogFrames = 0;
        this.lastDiagDecodedFrames = 0;
        this.lastDiagReceivedFrames = 0;

        // Stop latency reporting
        if (this.latencyReportTimer !== null) {
            clearInterval(this.latencyReportTimer);
            this.latencyReportTimer = null;
        }

        // Remove visibility subscription
        if (this.visibilitySubscription) {
            this.visibilitySubscription.unsubscribe();
            this.visibilitySubscription = null;
        }

        this.stopPull();

        // Close all pending frames
        for (const frame of this.pendingFrames) {
            try {
                frame.close();
            } catch {
                // Ignore
            }
        }
        this.pendingFrames = [];
        this.bufferSize = 0;

        // Stop decoder worker
        if (this.decoderWorker) {
            try {
                await this.decoderWorker.stop();
            } catch {
                // Ignore
            }
            this.decoderWorker.dispose();
            this.decoderWorker = null;
        }
        if (this.decoderWorkerInstance) {
            this.decoderWorkerInstance.terminate();
            this.decoderWorkerInstance = null;
        }

        debugLog?.log(`VideoPlayer stopped for stream ${this.streamId}`);
    }

    private reportLatencyTick(): void {
        if (!this.isPlaying)
            return;

        if (this.lastRenderedOffsetMs <= 0) {
            warnLog?.log(`reportLatencyTick: skip — lastRendered=${this.lastRenderedOffsetMs.toFixed(0)}`);
            return;
        }
        const streamOffsetMs = this.lastRenderedOffsetMs;

        const nowMs = ServerClock.now();
        const recordedAtMs = this.startedAtMs + this.lastRenderedOffsetMs;
        const latencyMs = nowMs - recordedAtMs;
        warnLog?.log(
            `LATENCY: authorId=${this.authorId}, streamId=${this.streamId}, ` +
            `now=${nowMs.toFixed(0)}, recorded=${recordedAtMs.toFixed(0)} ` +
            `(startedAt=${this.startedAtMs.toFixed(0)}+offset=${this.lastRenderedOffsetMs.toFixed(0)}), ` +
            `latency=${latencyMs.toFixed(0)}ms`);

        // Decoder diagnostics
        if (this.decoderWorker) {
            void this.decoderWorker.getStats().then(ds => {
                const recvDelta = this.receivedFrameCount - this.lastDiagReceivedFrames;
                const decodedDelta = ds.decodedFrames - this.lastDiagDecodedFrames;
                this.lastDiagReceivedFrames = this.receivedFrameCount;
                this.lastDiagDecodedFrames = ds.decodedFrames;

                warnLog?.log(
                    `VIDEO_DECODE: median=${ds.medianDecodeTime.toFixed(1)}ms avg=${ds.averageDecodeTime.toFixed(1)}ms ` +
                    `e2e=${this.pipelineLatencyMs.toFixed(0)}ms buf=${this.pendingFrames.length} ` +
                    `recv=${recvDelta} decoded=${decodedDelta} drop=${ds.droppedFrames} ` +
                    `res=${ds.resolution} hw=${ds.hardwareAcceleration}`);

                // A/V sync diagnostics
                const audioState = AudioVideoSync.get(this.authorId);
                if (audioState) {
                    const audioPlayingAtMs = AudioVideoSync.interpolatePlayingAt(audioState) * 1000;
                    const targetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs)
                        + audioPlayingAtMs - this.pipelineLatencyMs;
                    const avDriftMs = this.lastRenderedOffsetMs - targetVideoOffsetMs;
                    warnLog?.log(
                        `AV_SYNC: drift=${avDriftMs.toFixed(0)}ms ` +
                        `(videoOffset=${this.lastRenderedOffsetMs.toFixed(0)}ms, ` +
                        `targetOffset=${targetVideoOffsetMs.toFixed(0)}ms, ` +
                        `audioPlayingAt=${audioPlayingAtMs.toFixed(0)}ms, ` +
                        `audioState=${audioState.playbackState})`);
                } else {
                    warnLog?.log(`AV_SYNC: no audio state for authorId=${this.authorId}`);
                }
            });
        }

        const connection = VideoStreamer.connection;
        const sessionToken = SessionTokens.current;
        if (connection) {
            try {
                void connection.invoke('ReportVideoLatency', sessionToken, this.streamId, streamOffsetMs);
            } catch (e) {
                warnLog?.log('reportLatencyTick error:', e);
            }
        }
    }

    private async reportPlaying(offsetMs: number, isBufferLow: boolean): Promise<void> {
        try {
            await this.blazorRef.invokeMethodAsync('OnPlaying', offsetMs, isBufferLow);
        } catch (e) {
            warnLog?.log('reportPlaying error:', e);
        }
    }

    private async reportEnded(error?: string): Promise<void> {
        try {
            debugLog?.log(`VideoPlayer reporting ended for stream ${this.streamId}:`, error);
            await this.blazorRef.invokeMethodAsync('OnEnded', error ?? null);
        } catch (e) {
            warnLog?.log('reportEnded error:', e);
        }
    }
}
