import { Log } from 'logging';

const { debugLog, warnLog, errorLog } = Log.get('VideoPlayer');

export interface RemoteVideoStream {
    streamId: string;
    authorId: string;
    player: VideoPlayer;
    canvas: HTMLCanvasElement;
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

    // Buffering state
    private bufferSize = 0;
    private readonly maxBufferSize = 5; // frames
    private lastReportedBufferLow = true;

    // Animation frame for rendering
    private animationFrameId: number | null = null;

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
        this.initDecoder(codec, width, height, codecSettings);
    }

    private async initDecoder(codec: string, width: number, height: number, codecSettings: string): Promise<void> {
        if (!this.supportsWebCodecs()) {
            warnLog?.log('WebCodecs not supported, using canvas fallback');
            return;
        }

        try {
            // Map codec name to WebCodecs codec string
            const codecString = this.mapCodecToWebCodecs(codec);
            debugLog?.log(`Initializing WebCodecs decoder with codec: ${codecString}`);

            // Decode codec settings (base64 encoded SPS/PPS for H.264)
            let description: ArrayBuffer | undefined;
            if (codecSettings) {
                const binaryString = atob(codecSettings);
                const bytes = new Uint8Array(binaryString.length);
                for (let i = 0; i < binaryString.length; i++) {
                    bytes[i] = binaryString.charCodeAt(i);
                }
                description = bytes.buffer;
                debugLog?.log(`Codec settings decoded: ${bytes.length} bytes`);
            }

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
                output: (frame) => this.onFrameDecoded(frame),
                error: (e) => this.onDecoderError(e),
            });

            this.decoder.configure(this.decoderConfig);
            debugLog?.log('WebCodecs decoder configured');
        } catch (error) {
            errorLog?.log('Failed to initialize WebCodecs decoder:', error);
        }
    }

    private supportsWebCodecs(): boolean {
        return typeof VideoDecoder !== 'undefined';
    }

    private mapCodecToWebCodecs(codec: string): string {
        // Map common codec names to WebCodecs codec strings
        const codecMap: Record<string, string> = {
            'h264': 'avc1.42E01E', // H.264 Baseline profile
            'avc1': 'avc1.42E01E',
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
        return 'avc1.42E01E'; // Default to H.264 Baseline
    }

    private onFrameDecoded(frame: VideoFrame): void {
        this.pendingFrames.push(frame);
        this.bufferSize++;

        // Update buffer state
        const isBufferLow = this.bufferSize < 2;
        if (isBufferLow !== this.lastReportedBufferLow) {
            this.lastReportedBufferLow = isBufferLow;
            this.reportPlaying(frame.timestamp / 1000, isBufferLow);
        }

        // Render the frame
        this.renderNextFrame();
    }

    private onDecoderError(error: Error): void {
        errorLog?.log('Decoder error:', error);
        this.reportEnded(error.message);
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

    public async pushFrame(
        frameData: Uint8Array,
        timestampMs: number,
        durationMs: number,
        isKeyFrame: boolean
    ): Promise<void> {
        if (!this.isPlaying) {
            return;
        }

        if (this.decoder && this.decoder.state === 'configured') {
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
            // Fallback: try to decode with canvas-based approach
            // For now, just log - full fallback would require separate implementation
            warnLog?.log('Decoder not available, frame dropped');
        }
    }

    public start(): void {
        if (this.isPlaying) return;

        this.isPlaying = true;
        debugLog?.log(`VideoPlayer started for stream ${this.streamId}`);

        // Report initial playing state
        this.reportPlaying(0, true);
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
            } catch (e) {
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
            } catch (e) {
                // Ignore
            }
            this.decoder = null;
        }

        debugLog?.log(`VideoPlayer stopped for stream ${this.streamId}`);
    }

    private async reportPlaying(offsetMs: number, isBufferLow: boolean): Promise<void> {
        try {
            await this.blazorRef?.invokeMethodAsync('OnPlaying', offsetMs, isBufferLow);
        } catch (e) {
            warnLog?.log('reportPlaying error:', e);
        }
    }

    private async reportEnded(error?: string): Promise<void> {
        try {
            debugLog?.log(`VideoPlayer reporting ended for stream ${this.streamId}:`, error);
            await this.blazorRef?.invokeMethodAsync('OnEnded', error ?? null);
        } catch (e) {
            warnLog?.log('reportEnded error:', e);
        }
    }
}
