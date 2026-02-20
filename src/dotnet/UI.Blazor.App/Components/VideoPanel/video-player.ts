import * as signalR from '@microsoft/signalr';
import { decode } from '@msgpack/msgpack';
import { Log } from 'logging';
import { fastRaf } from 'fast-raf';
import { VideoStreamer } from '../../Services/Video/video-streamer';
import { SessionTokens } from '../../../UI.Blazor/Services/Security/session-tokens';
import { getAudioSyncState, interpolatePlayingAt } from 'audio-video-sync';

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

interface PendingChunk {
    frameData: Uint8Array;
    timestampMs: number;
    durationMs: number;
    isKeyFrame: boolean;
    description?: Uint8Array;
}

export class VideoPlayer {
    private blazorRef: DotNet.DotNetObject;
    private streamId: string;
    private authorId: string;
    private canvas: HTMLCanvasElement;
    private canvasCtx: CanvasRenderingContext2D | null = null;

    // WebCodecs decoder (when available)
    private decoder: VideoDecoder | null = null;
    private decoderConfig: VideoDecoderConfig | null = null;
    private pendingFrames: VideoFrame[] = [];
    private isPlaying = false;

    // Buffer chunks until we receive a keyframe with description
    private waitingForKeyframe = true;
    private pendingChunks: PendingChunk[] = [];

    // Buffering state
    private bufferSize = 0;
    private readonly maxBufferSize = 5; // frames
    private lastReportedBufferLow = true;

    // SignalR pull subscription
    private pullSubscription: signalR.ISubscription<Uint8Array[]> | null = null;

    // Frame pacing state
    private playbackStartTime = 0;     // wall-clock ms (performance.now) when first frame rendered
    private firstFrameTimestamp = 0;    // timestamp of first decoded frame (microseconds)
    private renderKey: string;
    private renderFrameCount = 0;       // count of rendered frames (for periodic logging)
    private receivedFrameCount = 0;     // count of received frames (for periodic logging)
    private lastSyncLogTime = 0;        // throttle sync logging

    // Latency measurement
    private lastRenderedOffsetMs = 0;   // offset of the latest decoded frame (ms from stream start)
    private latencyReportTimer: ReturnType<typeof setInterval> | null = null;

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

        // Initialize decoder
        void this.initDecoder(codec, width, height, codecSettings);
    }

    private async initDecoder(codec: string, width: number, height: number, codecSettings: string): Promise<void> {
        if (!this.supportsWebCodecs()) {
            warnLog?.log('WebCodecs not supported, using canvas fallback');
            return;
        }

        try {
            // Decode codec settings (base64 encoded SPS/PPS for H.264)
            let description: ArrayBuffer | undefined;
            if (codecSettings) {
                console.log(`[VideoPlayer] Received codecSettings: ${codecSettings.length} base64 chars`);
                console.log(`[VideoPlayer] codecSettings base64: ${codecSettings}`);
                const binaryString = atob(codecSettings);
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {
                    bytes[i] = binaryString.charCodeAt(i);
                }
                description = bytes.buffer;
                // Log first few bytes for debugging
                const hexBytes = Array.from(bytes.slice(0, 20)).map(b => b.toString(16).padStart(2, '0')).join(' ');
                console.log(`[VideoPlayer] Decoded description: ${bytes.length} bytes`);
                console.log(`[VideoPlayer] Description first 20 bytes (hex): ${hexBytes}`);
            }

            // Map codec name to WebCodecs codec string (pass description to extract actual profile)
            const codecString = this.mapCodecToWebCodecs(codec, description);
            debugLog?.log(`Initializing WebCodecs decoder with codec: ${codecString}`);

            this.decoderConfig = {
                codec: codecString,
                codedWidth: width,
                codedHeight: height,
                description: description,
                hardwareAcceleration: 'prefer-hardware',
                optimizeForLatency: true,
            };

            // Check if codec is supported
            const support = await VideoDecoder.isConfigSupported(this.decoderConfig);
            if (!support.supported) {
                errorLog?.log(`Codec ${codecString} not supported`);
                return;
            }

            this.decoder = new VideoDecoder({
                output: (frame: VideoFrame) => this.onFrameDecoded(frame),
                error: (e: DOMException) => this.onDecoderError(e),
            });

            this.decoder.configure(this.decoderConfig);
            debugLog?.log('WebCodecs decoder configured');

            // If we have codec settings, we don't need to wait for keyframe with description
            if (codecSettings) {
                this.waitingForKeyframe = false;
                console.log('[VideoPlayer] Decoder configured with codecSettings from VideoFormat, not waiting for keyframe with description');
            }
        } catch (error) {
            errorLog?.log('Failed to initialize WebCodecs decoder:', error);
        }
    }

    private supportsWebCodecs(): boolean {
        return typeof VideoDecoder !== 'undefined';
    }

    private mapCodecToWebCodecs(codec: string, description?: ArrayBuffer): string {
        // If we have an avcC description, extract the actual codec profile from it
        if (description && description.byteLength >= 4 && (codec.toLowerCase() === 'h264' || codec.toLowerCase() === 'avc1')) {
            const bytes = new Uint8Array(description);
            // avcC structure: configurationVersion(1), profileIndication(1), profileCompatibility(1), levelIndication(1), ...
            const profileIndication = bytes[1];
            const profileCompatibility = bytes[2];
            const levelIndication = bytes[3];
            const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
            console.log(`[VideoPlayer] Built codec string from avcC: ${codecString} (profile=${profileIndication}, compat=${profileCompatibility}, level=${levelIndication})`);
            return codecString;
        }

        // Map common codec names to WebCodecs codec strings
        const codecMap: Record<string, string> = {
            'h264': 'avc1.640028', // H.264 High profile, Level 4.0 (common default)
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
        // If codec already looks like a WebCodecs string, use it as-is
        if (codec.includes('.')) {
            return codec;
        }
        return 'avc1.640028'; // Default to H.264 High profile
    }

    private onFrameDecoded(frame: VideoFrame): void {
        this.pendingFrames.push(frame);
        this.bufferSize++;
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
        // Key dedup: if already scheduled for this player, fastRaf returns false (no-op)
        fastRaf({ write: () => this.onRenderFrame(), key: this.renderKey });
    }

    private onRenderFrame(): void {
        if (!this.isPlaying || this.pendingFrames.length === 0) return;

        const now = performance.now();

        // Initialize timing anchor on first frame
        if (this.playbackStartTime === 0) {
            this.playbackStartTime = now;
            this.firstFrameTimestamp = this.pendingFrames[0].timestamp; // μs
        }

        this.renderFrameCount++;

        // Compute target — audio-driven when available, wall-clock fallback
        let targetTimestamp: number;
        const audioState = getAudioSyncState(this.authorId);
        if (audioState) {
            const audioPlayingAtMs = interpolatePlayingAt(audioState) * 1000;
            const targetVideoOffsetMs = (audioState.recordedAtMs - this.startedAtMs) + audioPlayingAtMs;
            targetTimestamp = targetVideoOffsetMs * 1000; // ms → μs

            // Re-anchor wall-clock for smooth fallback when audio disappears
            this.playbackStartTime = now;
            this.firstFrameTimestamp = targetTimestamp;

            // Periodic sync logging
            if (now - this.lastSyncLogTime > 1000) {
                this.lastSyncLogTime = now;
                const driftMs = this.lastRenderedOffsetMs - targetVideoOffsetMs;
                debugLog?.log(
                    `audioSync: targetOffsetMs=${targetVideoOffsetMs.toFixed(0)}, ` +
                    `lastRenderedOffsetMs=${this.lastRenderedOffsetMs.toFixed(0)}, ` +
                    `driftMs=${driftMs.toFixed(0)}, pendingFrames=${this.pendingFrames.length}`);
            }
        } else {
            // Wall-clock pacing (no audio to sync to)
            const elapsedUs = (now - this.playbackStartTime) * 1000; // ms → μs
            targetTimestamp = this.firstFrameTimestamp + elapsedUs;

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
                frameToRender.close(); // Drop skipped frame
                this.bufferSize--;
            }
            frameToRender = this.pendingFrames.shift()!;
        }

        if (frameToRender) {
            this.bufferSize--;
            this.lastRenderedOffsetMs = frameToRender.timestamp / 1000; // μs → ms
            this.drawFrame(frameToRender);
            frameToRender.close();
        }

        this.updateBufferState();

        // Re-schedule if more frames pending
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
        if (!this.isPlaying) {
            return;
        }

        // If we're waiting for a keyframe with description, buffer chunks
        if (this.waitingForKeyframe) {
            console.log(`[VideoPlayer] Received frame: isKey=${isKeyFrame}, descLen=${description?.length ?? 0}, dataLen=${frameData.length}`);
            if (isKeyFrame && description && description.length > 0) {
                // Got the keyframe with description - configure decoder and process buffered chunks
                console.log(`[VideoPlayer] GOT KEYFRAME WITH DESCRIPTION: ${description.length} bytes, processing ${this.pendingChunks.length} buffered chunks`);
                this.waitingForKeyframe = false;

                // Configure decoder with description
                // Note: description.buffer may be larger than the actual data if it's a view,
                // so we need to slice it properly
                if (this.decoder && this.decoderConfig) {
                    const descBuffer = description.buffer.slice(
                        description.byteOffset,
                        description.byteOffset + description.byteLength
                    );
                    const newConfig: VideoDecoderConfig = {
                        ...this.decoderConfig,
                        description: descBuffer,
                    };
                    this.decoder.configure(newConfig);
                    this.decoderConfig = newConfig;
                }

                // Process the keyframe first
                this.decodeChunk(frameData, timestampMs, durationMs, isKeyFrame);

                // Clear buffered delta frames (they're useless without the keyframe before them)
                this.pendingChunks = [];
            } else {
                // Buffer delta frames while waiting (but limit buffer size)
                if (this.pendingChunks.length < 30) {
                    this.pendingChunks.push({ frameData, timestampMs, durationMs, isKeyFrame, description });
                }
                if (this.pendingChunks.length <= 5 || this.pendingChunks.length % 30 === 0)
                    console.log(`[VideoPlayer] Waiting for keyframe with description, buffered ${this.pendingChunks.length} chunks (isKey=${isKeyFrame}, hasDesc=${!!description})`);
            }
            return;
        }

        // If we receive a new keyframe with description, reconfigure the decoder
        // only when the description actually changed (avoids resetting pacing anchor
        // every ~1s keyframe when the codec config is identical)
        if (isKeyFrame && description && description.length > 0) {
            const currentDesc = this.decoderConfig?.description;
            const descChanged = !currentDesc || !arrayBufferEqual(currentDesc, description);
            if (descChanged) {
                debugLog?.log(`Reconfiguring decoder with new description: ${description.length} bytes`);
                if (this.decoder && this.decoderConfig) {
                    const descBuffer = description.buffer.slice(
                        description.byteOffset,
                        description.byteOffset + description.byteLength
                    );
                    const newConfig: VideoDecoderConfig = {
                        ...this.decoderConfig,
                        description: descBuffer,
                    };
                    this.decoder.configure(newConfig);
                    this.decoderConfig = newConfig;
                }
                this.playbackStartTime = 0; // Reset pacing anchor to re-sync
            }
        }

        this.decodeChunk(frameData, timestampMs, durationMs, isKeyFrame);
    }

    private decodeChunk(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean
    ): void {
        if (this.decoder?.state === 'configured') {
            try {
                const chunk = new EncodedVideoChunk({
                    type: isKeyFrame ? 'key' : 'delta',
                    timestamp: timestampMs * 1000, // Convert to microseconds
                    duration: durationMs * 1000,
                    data: frameData,
                });

                this.decoder.decode(chunk);
            } catch (error) {
                errorLog?.log('Error decoding chunk:', error);
            }
        } else {
            warnLog?.log('Decoder not available, frame dropped');
        }
    }

    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
        debugLog?.log(`VideoPlayer started for stream ${this.streamId}`);

        // Report initial playing state
        void this.reportPlaying(0, true);
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

        const streamResult = connection.stream<Uint8Array[]>(
            'GetVideo', sessionToken, streamId, skipToMs);

        // Start latency report timer now that we're receiving frames
        this.latencyReportTimer ??= setInterval(() => this.reportLatencyTick(), 10_000);

        this.pullSubscription = streamResult.subscribe({
            next: (batch: Uint8Array[]) => {
                for (const frameBytes of batch) {
                    this.processReceivedFrame(frameBytes);
                }
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
            if (this.receivedFrameCount % 100 === 1) {
                debugLog?.log(
                    `processReceivedFrame #${this.receivedFrameCount}: offsetMs=${offsetMs.toFixed(0)}, ` +
                    `durationMs=${durationMs.toFixed(1)}, isKey=${isKeyFrame}, dataLen=${data.length}`);
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

        // Stop latency reporting
        if (this.latencyReportTimer !== null) {
            clearInterval(this.latencyReportTimer);
            this.latencyReportTimer = null;
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

        // Close decoder
        if (this.decoder) {
            try {
                await this.decoder.flush();
                this.decoder.close();
            } catch {
                // Ignore
            }
            this.decoder = null;
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
        // lastRenderedOffsetMs is already 0-based from stream start (server frame offset)
        const streamOffsetMs = this.lastRenderedOffsetMs;

        const nowMs = Date.now();
        const recordedAtMs = this.startedAtMs + this.lastRenderedOffsetMs;
        const latencyMs = nowMs - recordedAtMs;
        warnLog?.log(
            `LATENCY: authorId=${this.authorId}, streamId=${this.streamId}, ` +
            `now=${nowMs.toFixed(0)}, recorded=${recordedAtMs.toFixed(0)} ` +
            `(startedAt=${this.startedAtMs.toFixed(0)}+offset=${this.lastRenderedOffsetMs.toFixed(0)}), ` +
            `latency=${latencyMs.toFixed(0)}ms`);

        // Report latency via SignalR hub (same connection as GetVideo) so peerId matches
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
