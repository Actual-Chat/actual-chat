import { Log } from 'logging';

const { debugLog, warnLog, errorLog } = Log.get('VideoPlayer');

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

    // Animation frame for rendering
    private animationFrameId: number | null = null;

    /** Creates a new VideoPlayer instance for Blazor interop */
    static create(
        canvas: HTMLCanvasElement,
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string
    ): VideoPlayer {
        return new VideoPlayer(blazorRef, streamId, authorId, codec, width, height, codecSettings, canvas);
    }

    constructor(
        blazorRef: DotNet.DotNetObject,
        streamId: string,
        authorId: string,
        codec: string,
        width: number,
        height: number,
        codecSettings: string,
        canvas: HTMLCanvasElement
    ) {
        this.blazorRef = blazorRef;
        this.streamId = streamId;
        this.authorId = authorId;
        this.canvas = canvas;
        this.canvasCtx = canvas.getContext('2d');

        // Set canvas size
        canvas.width = width || 1280;
        canvas.height = height || 720;

        debugLog?.log(`VideoPlayer created for stream ${streamId}, codec: ${codec}, size: ${width}x${height}`);

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

        // Update buffer state
        const isBufferLow = this.bufferSize < 2;
        if (isBufferLow !== this.lastReportedBufferLow) {
            this.lastReportedBufferLow = isBufferLow;
            void this.reportPlaying(frame.timestamp / 1000, isBufferLow);
        }

        // Render the frame
        this.renderNextFrame();
    }

    private onDecoderError(error: Error): void {
        errorLog?.log('Decoder error:', error);
        void this.reportEnded(error.message);
    }

    private renderNextFrame(): void {
        if (this.pendingFrames.length === 0) {
            return;
        }

        const frame = this.pendingFrames.shift();
        if (!frame) return;

        this.bufferSize--;

        if (this.canvasCtx) {
            try {
                // Update canvas size if needed
                if (this.canvas.width !== frame.displayWidth || this.canvas.height !== frame.displayHeight) {
                    this.canvas.width = frame.displayWidth;
                    this.canvas.height = frame.displayHeight;
                    debugLog?.log(`Canvas resized to ${frame.displayWidth}x${frame.displayHeight}`);
                }

                this.canvasCtx.drawImage(frame, 0, 0);
            } catch (error) {
                errorLog?.log('Error rendering frame:', error);
            } finally {
                frame.close();
            }
        } else {
            frame.close();
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
        // Note: description.buffer may be larger than the actual data if it's a view,
        // so we need to slice it properly
        if (isKeyFrame && description && description.length > 0) {
            debugLog?.log(`Reconfiguring decoder with new description: ${description.length} bytes`);
            if (this.decoder && this.decoderConfig) {
                // Create a proper ArrayBuffer from the Uint8Array (handles views correctly)
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

    public async stop(): Promise<void> {
        if (!this.isPlaying) return;

        this.isPlaying = false;

        if (this.animationFrameId !== null) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }

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
