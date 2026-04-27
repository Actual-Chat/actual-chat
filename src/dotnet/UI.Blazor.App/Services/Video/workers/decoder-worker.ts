/**
 * Decoder Worker (Universal - Chrome & Safari)
 * Handles video decoding in a dedicated worker thread using RPC communication.
 * Receives encoded chunks and outputs decoded frames via RPC callbacks.
 *
 * Used by video-player.ts for off-main-thread decoding.
 */

import { rpcClientServer, rpcNoWait } from 'rpc';
import type { DecoderWorker, DecoderWorkerCallbacks, RawChunkMessage } from './decoder-worker-contract';
import { type DecoderConfig, type DecoderStats, WebCodecsDecoder } from '../webcodecs-decoder';
import type { EncodedChunkData } from '../webcodecs-encoder';
import { extractHVCC } from '../hevc-parser';
import { getLogs } from 'logging';
import { WorkerMstgSelector } from './worker-mstg-selector';
import { Api, streamingApi } from 'api';
import { WorkerConnectivityUI } from '../../../Components/AudioRecorder/workers/worker-connectivity-ui';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

// Worker state
let decoder: WebCodecsDecoder | null = null;
let processing = false;
let decoderConfigured = false;
let pendingChunks: EncodedChunkData[] = [];
let currentDecoderConfig: DecoderConfig | null = null;
// eslint-disable-next-line @typescript-eslint/no-unused-vars
let frameCount = 0;
let lastRawDescription: ArrayBuffer | null = null;

// Stream-based input reader loop promise (for cleanup)
let streamReadLoopPromise: Promise<void> | null = null;

// Off-thread MSTG render path: when set, decoded frames are routed into the
// selector instead of being emitted to main via onDecodedFrame.
let mstgSelector: WorkerMstgSelector | null = null;

// In-worker Fusion RPC pull state (§9). When pullActive, the worker iterates
// `streamingApi.streamServer.GetVideo(...)` itself and feeds chunks into the
// decoder — main never sees per-frame work on this path.
let pullActive = false;
let pullAbortController: AbortController | null = null;
let pullStartedAtMs = 0;
let pullRetryCount = 0;
let pullSequenceNumber = 0;
const PULL_LATENCY_REPORT_INTERVAL_MS = 2000;
let lastLatencyReportAt = 0;
let apiInitialized = false;

function bufferEqual(a: ArrayBuffer, b: ArrayBuffer): boolean {
    if (a.byteLength !== b.byteLength) return false;
    const viewA = new Uint8Array(a);
    const viewB = new Uint8Array(b);
    for (let i = 0; i < viewA.length; i++) {
        if (viewA[i] !== viewB[i]) return false;
    }
    return true;
}

// Chunk ordering state to prevent out-of-order decoding issues
let nextExpectedSequence = 0;
const reorderBuffer = new Map<number, EncodedChunkData>();
let lastKeyframeSequence = -1;
const MAX_REORDER_GAP = 5; // If we receive packets 5+ ahead, assume intermediate ones are lost
let waitingForKeyframe = false; // Flag to indicate we're in error recovery mode

// Process buffered chunks in sequence order
function processBufferedChunks(): void {
    while (reorderBuffer.has(nextExpectedSequence)) {
        const chunk = reorderBuffer.get(nextExpectedSequence)!;
        reorderBuffer.delete(nextExpectedSequence);
        decodeChunk(chunk);
        nextExpectedSequence++;
    }
}

// Extract codec family prefix for comparison (e.g., 'avc1' from 'avc1.640028', 'av01' from 'av01.0.08M.08')
function codecFamily(codec: string): string {
    return codec.substring(0, 4);
}

// Handle codec change: flush+close old decoder, create new one with updated config
function handleCodecChange(chunkData: EncodedChunkData): void {
    const newCodec = chunkData.codec!;
    const oldCodec = currentDecoderConfig!.codec;
    infoLog?.log(`Codec change detected: ${oldCodec} -> ${newCodec}, reconfiguring decoder`);

    // 1. Flush + close old decoder
    if (decoder) {
        try {
            if (decoder.getState() === 'configured') {
                // Can't await in sync context, just close
                decoder.close();
            }
        } catch (error) {
            warnLog?.log('Error closing old decoder during codec switch:', error);
        }
    }

    // 2. Update config with new codec
    currentDecoderConfig = { ...currentDecoderConfig!, codec: newCodec, description: undefined };

    // 3. Create new decoder
    decoder = new WebCodecsDecoder(
        { ...currentDecoderConfig, description: undefined },
        emitDecodedFrame,
        (error) => {
            errorLog?.log('Decoder error:', error);
        }
    );
    decoder.initialize();

    // 4. Reset state
    decoderConfigured = false;
    pendingChunks = [];
    reorderBuffer.clear();
    lastKeyframeSequence = -1;
    waitingForKeyframe = false;
    nextExpectedSequence = chunkData.sequenceNumber;

    infoLog?.log(`Decoder reconfigured for codec ${newCodec}, resuming at sequence #${chunkData.sequenceNumber}`);
}

// Decode a single chunk (guaranteed to be in sequence order)
function decodeChunk(chunkData: EncodedChunkData): void {
    const seq = chunkData.sequenceNumber;

    try {
    // Auto-detect codec change from keyframe data
        if (chunkData.type === 'key' && chunkData.codec && currentDecoderConfig) {
            const incomingFamily = codecFamily(chunkData.codec);
            const currentFamily = codecFamily(currentDecoderConfig.codec);
            if (incomingFamily !== currentFamily) {
                handleCodecChange(chunkData);
                // Fall through to normal keyframe processing below
            }
        }

        // Track keyframes for decoder recovery
        if (chunkData.type === 'key') {
            lastKeyframeSequence = seq;
        }

        // If decoder is closed and this is a keyframe, attempt recovery
        if (decoder && decoder.getState() === 'closed' && chunkData.type === 'key') {
            // HEVC/AVC require description on every configure() — bake it into the
            // recovery config so the next keyframe doesn't fail with
            // "A key frame is required after configure()" DataError.
            const metadataDesc = chunkData.metadata?.decoderConfig?.description;
            const recoveryDescription = metadataDesc
                ?? lastRawDescription
                ?? currentDecoderConfig?.description;
            infoLog?.log(`Decoder closed, attempting recovery with keyframe #${seq} (descLen=${
                recoveryDescription ? (recoveryDescription as ArrayBuffer).byteLength : 0})`);

            try {
                const recoveryConfig: DecoderConfig = recoveryDescription
                    ? { ...currentDecoderConfig!, description: recoveryDescription }
                    : { ...currentDecoderConfig!, description: undefined };
                decoder = new WebCodecsDecoder(
                    recoveryConfig,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                    }
                );

                decoder.initialize();
                infoLog?.log(`Decoder recovered at keyframe #${seq}`);
                decoderConfigured = !!recoveryDescription;
                if (recoveryDescription)
                    lastRawDescription = (recoveryDescription as ArrayBuffer).slice(0);
            } catch (error) {
                errorLog?.log('Failed to recover decoder:', error);
                return;
            }
        }

        // If decoder is still closed (not a keyframe or recovery failed), skip this chunk
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Handle first keyframe with metadata
        if (!decoderConfigured && chunkData.type === 'key') {
            infoLog?.log(`First keyframe #${seq} received`);

            let description: AllowSharedBufferSource | undefined;

            // Try to get description from encoder metadata first
            if (chunkData.metadata?.decoderConfig?.description) {
                infoLog?.log('Using description from encoder metadata');
                description = chunkData.metadata.decoderConfig.description;
            }
            // For HEVC, try manual HVCC extraction as fallback
            else if (currentDecoderConfig?.codec.startsWith('hev1') || currentDecoderConfig?.codec.startsWith('hvc1')) {
                infoLog?.log('Attempting manual HVCC extraction for HEVC');
                const hvcc = extractHVCC(chunkData.chunk);
                if (hvcc) {
                    infoLog?.log('Successfully extracted HVCC from bitstream');
                    description = hvcc;
                } else {
                    warnLog?.log('Failed to extract HVCC, decoder may fail');
                }
            } else {
                infoLog?.log('No metadata description - decoder will auto-configure');
            }

            // Reconfigure decoder with description if available
            if (description && decoder) {
                infoLog?.log('Reconfiguring decoder with description');
                decoder.updateDescription(description);
            }

            // Mark as configured so we start decoding
            decoderConfigured = true;

            // Decode the keyframe
            if (decoder) {
                decoder.decode(chunkData);
                infoLog?.log(`First keyframe #${seq} decoded successfully`);
            }

            // Process any buffered chunks from before configuration
            if (pendingChunks.length > 0) {
                infoLog?.log('Processing', pendingChunks.length, 'buffered chunks');
                for (const bufferedChunk of pendingChunks) {
                    if (decoder) {
                        decoder.decode(bufferedChunk);
                    }
                }
                pendingChunks = [];
            }

            return;
        }

        // Check decoder state before attempting to decode
        if (decoder && decoder.getState() === 'closed') {
            if (chunkData.type === 'key') {
                infoLog?.log(`Decoder in error state, but received keyframe #${seq}`);
            } else {
                warnLog?.log(`Decoder in error state, dropping delta chunk #${seq}`);
                return;
            }
        }

        // Decode chunks directly
        if (decoderConfigured && decoder) {
            decoder.decode(chunkData);
        } else {
            // Buffer until decoder is configured with first keyframe
            debugLog?.log('Buffering chunk until configured');
            pendingChunks.push(chunkData);
        }
    } catch (error) {
        errorLog?.log(`Error decoding chunk #${seq}:`, error);

        // If we have a recent keyframe, try to recover
        if (lastKeyframeSequence >= 0 && reorderBuffer.has(lastKeyframeSequence)) {
            infoLog?.log(`Attempting recovery from buffered keyframe #${lastKeyframeSequence}`);
        }
    }
}

/**
 * Emit a decoded frame to the appropriate output (stream or RPC callback).
 */
function emitDecodedFrame(frame: VideoFrame): void {
    frameCount++;
    if (mstgSelector) {
        mstgSelector.onDecoded(frame);
        return;
    }
    void callbacks.onDecodedFrame(frame, rpcNoWait);
}

/**
 * Helper: create WebCodecsDecoder instance with standard frame callback
 */
function createDecoder(config: DecoderConfig): WebCodecsDecoder {
    return new WebCodecsDecoder(
        { ...config, description: undefined },
        emitDecodedFrame,
        (error) => {
            errorLog?.log('Decoder error:', error);
        }
    );
}

// In-worker pull loop. Iterates `streamingApi.streamServer.GetVideo(...)`,
// feeds each frame into `serverImpl.decodeRawChunk` (which handles codec
// change, reorder, and decode), and retries with backoff on empty / error.
// Mirror of the main-thread `startPull` from video-player.ts:1265-1334.
async function runPullLoop(streamId: string, skipToMs: number): Promise<void> {
    const ac = new AbortController();
    pullAbortController = ac;
    const skipToTicks = Math.round(skipToMs * 10000); // ms → .NET TimeSpan ticks
    let pullFrameCount = 0;
    let lastArrivedOffsetMs = 0;

    try {
        infoLog?.log(`pull: GetVideo(${streamId}, skipTo=${skipToMs}ms)`);
        const stream = await streamingApi.streamServer.GetVideo(streamId, skipToTicks);

        for await (const frame of stream) {
            if (ac.signal.aborted || !pullActive) break;
            pullFrameCount++;
            pullRetryCount = 0;

            const offsetMs = frame.Offset / 10000;
            const durationMs = frame.Duration / 10000;
            if (offsetMs > lastArrivedOffsetMs) lastArrivedOffsetMs = offsetMs;

            const data = frame.Data;
            const dataBuffer = new ArrayBuffer(data.byteLength);
            new Uint8Array(dataBuffer).set(data);
            let descBuffer: ArrayBuffer | undefined;
            const desc = frame.Description;
            if (desc && desc.length > 0) {
                descBuffer = new ArrayBuffer(desc.byteLength);
                new Uint8Array(descBuffer).set(desc);
            }

            await serverImpl.decodeRawChunk(
                offsetMs * 1000,        // ms → μs
                durationMs * 1000,
                frame.IsKeyFrame,
                pullSequenceNumber++,
                dataBuffer,
                descBuffer,
            );

            const now = performance.now();
            if (now - lastLatencyReportAt > PULL_LATENCY_REPORT_INTERVAL_MS) {
                lastLatencyReportAt = now;
                void callbacks.onLatencyReport(lastArrivedOffsetMs, rpcNoWait);
            }
        }

        if (ac.signal.aborted || !pullActive) return;

        if (pullFrameCount > 0) {
            infoLog?.log(`pull: completed normally after ${pullFrameCount} frames`);
            pullActive = false;
            void callbacks.onPullEnded(null, rpcNoWait);
        } else {
            // Empty stream — skipTo may exceed available data, retry with backoff.
            pullRetryCount++;
            const delay = Math.min(500 * pullRetryCount, 2000);
            warnLog?.log(`pull: empty stream, retry #${pullRetryCount} in ${delay}ms`);
            setTimeout(() => {
                if (!pullActive) return;
                const retrySkipToMs = Math.max(0, Date.now() - pullStartedAtMs);
                void runPullLoop(streamId, retrySkipToMs);
            }, delay);
        }
    } catch (err) {
        if (ac.signal.aborted || !pullActive) return;
        const message = err instanceof Error ? err.message : String(err);
        pullRetryCount++;
        const delay = Math.min(1000 * pullRetryCount, 5000);
        warnLog?.log(`pull: error (retry #${pullRetryCount} in ${delay}ms): ${message}`);
        setTimeout(() => {
            if (!pullActive) return;
            const retrySkipToMs = Math.max(0, Date.now() - pullStartedAtMs);
            void runPullLoop(streamId, retrySkipToMs);
        }, delay);
    }
}

// RPC Server Implementation
const serverImpl: DecoderWorker = {
    /**
   * Initialize the decoder
   */
    // eslint-disable-next-line
    initialize: async (config): Promise<void> => {
        try {
            infoLog?.log('Initializing decoder for codec:', config.codec,
                ', descriptionLen:', config.description
                    ? config.description.byteLength
                    : 'none');

            currentDecoderConfig = config;

            if (config.description) {
                // Create decoder WITH description — single configure() in AVCC mode.
                // Do NOT use createDecoder() which strips description, causing
                // double-configure (Annex B → AVCC) that breaks Chrome's VideoDecoder.
                decoder = new WebCodecsDecoder(
                    config,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                    }
                );
                decoder.initialize();
                decoderConfigured = true;
                infoLog?.log('Decoder initialized in AVCC mode (single configure)');
            } else {
                decoder = createDecoder(config);
                decoder.initialize();
                infoLog?.log('Decoder initialized without description');
            }

            processing = true;
            infoLog?.log('Ready to decode chunks');
        } catch (error) {
            errorLog?.log('Failed to initialize decoder:', error);
            throw error;
        }
    },

    /**
   * Initialize and start stream-based decoding.
   */
    // eslint-disable-next-line @typescript-eslint/require-await
    initializeWithStreams: async (
        config: DecoderConfig,
        chunkInputStream: ReadableStream<RawChunkMessage>,
    ): Promise<void> => {
        try {
            infoLog?.log('Initializing decoder (stream input, RPC output) for codec:', config.codec);

            currentDecoderConfig = config;

            // Output goes via RPC callback (onDecodedFrame) — no stream output writer.
            // Cross-worker VideoFrame transfer via postMessage+transfer works correctly,
            // unlike WritableStream which uses structured clone.

            if (config.description) {
                decoder = new WebCodecsDecoder(
                    config,
                    emitDecodedFrame,
                    (error) => { errorLog?.log('Decoder error:', error); }
                );
                decoder.initialize();
                decoderConfigured = true;
                infoLog?.log('Decoder initialized in AVCC mode (stream, single configure)');
            } else {
                decoder = createDecoder(config);
                decoder.initialize();
                infoLog?.log('Decoder initialized without description (stream)');
            }

            processing = true;

            // Start reading from input stream (async, runs in background)
            const inputReader = chunkInputStream.getReader();
            streamReadLoopPromise = (async () => {
                try {
                    while (processing) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                        const { done, value } = await inputReader.read();
                        if (done) {
                            infoLog?.log('Decoder stream input ended');
                            break;
                        }
                        // Reuse the existing decodeRawChunk logic
                        await serverImpl.decodeRawChunk(
                            value.timestamp,
                            value.duration,
                            value.isKeyFrame,
                            value.sequenceNumber,
                            value.data,
                            value.description
                        );
                    }
                } catch (error) {
                    if (processing) { // eslint-disable-line @typescript-eslint/no-unnecessary-condition
                        errorLog?.log('Decoder stream read error:', error);
                    }
                } finally {
                    try { inputReader.releaseLock(); } catch { /* ignore */ }
                }
            })();

            infoLog?.log('Ready to decode chunks (stream mode)');
        } catch (error) {
            errorLog?.log('Failed to initialize decoder stream mode:', error);
            throw error;
        }
    },

    /**
   * Stop the decoder
   */
    stop: async (): Promise<void> => {
        try {
            infoLog?.log('Stopping decoder...');

            processing = false;
            decoderConfigured = false;

            if (pullActive) {
                pullActive = false;
                if (pullAbortController) {
                    pullAbortController.abort();
                    pullAbortController = null;
                }
            }

            if (mstgSelector) {
                mstgSelector.dispose();
                mstgSelector = null;
            }

            // Wait for stream read loop to finish
            if (streamReadLoopPromise) {
                try { await streamReadLoopPromise; } catch { /* ignore */ }
                streamReadLoopPromise = null;
            }

            // Wait for in-flight chunks
            await new Promise(resolve => setTimeout(resolve, 200));

            // Flush and close decoder
            if (decoder) {
                try {
                    await decoder.flush();
                    decoder.close();
                    infoLog?.log('Decoder closed');
                } catch (error) {
                    warnLog?.log('Decoder close error:', error);
                }
            }

            infoLog?.log('Decoder stopped');

            // Reset state
            decoder = null;
            pendingChunks = [];
            currentDecoderConfig = null;
            frameCount = 0;
            lastRawDescription = null;
            nextExpectedSequence = 0;
            reorderBuffer.clear();
            lastKeyframeSequence = -1;
            waitingForKeyframe = false;
        } catch (error) {
            errorLog?.log('Failed to stop decoder:', error);
            throw error;
        }
    },

    /**
   * Decode an encoded chunk (legacy path — EncodedChunkData with EncodedVideoChunk)
   */
    // eslint-disable-next-line
    decodeChunk: async (chunkData): Promise<void> => {
        if (!processing) {
            warnLog?.log('Dropping chunk - not processing');
            return;
        }

        const seq = chunkData.sequenceNumber;

        // If we're waiting for a keyframe due to packet loss, drop all non-keyframe chunks
        if (waitingForKeyframe && chunkData.type !== 'key') {
            return;
        }

        // If this is a keyframe and we were waiting for one, reset recovery mode
        if (waitingForKeyframe && chunkData.type === 'key') {
            infoLog?.log(`Recovery keyframe #${seq} received`);
            waitingForKeyframe = false;
            reorderBuffer.clear();
            nextExpectedSequence = seq;
            decodeChunk(chunkData);
            nextExpectedSequence = seq + 1;
            return;
        }

        // Handle out-of-order delivery: buffer chunks until we can process in sequence
        if (seq !== -1 && seq !== nextExpectedSequence) {
            const gap = seq - nextExpectedSequence;
            debugLog?.log(`Out-of-order chunk #${seq} (expecting #${nextExpectedSequence}), gap:`, gap);
            reorderBuffer.set(seq, chunkData);

            if (gap >= MAX_REORDER_GAP) {
                warnLog?.log(`Gap of ${gap} detected, packet #${nextExpectedSequence} is likely lost`);

                let hasKeyframeInBuffer = false;
                let firstKeyframeSeq = -1;
                for (const [bufSeq, bufChunk] of reorderBuffer) {
                    if (bufChunk.type === 'key' && bufSeq > nextExpectedSequence) {
                        hasKeyframeInBuffer = true;
                        firstKeyframeSeq = firstKeyframeSeq === -1 ? bufSeq : Math.min(firstKeyframeSeq, bufSeq);
                    }
                }

                if (hasKeyframeInBuffer) {
                    infoLog?.log(`Found keyframe #${firstKeyframeSeq} in buffer, skipping to it`);
                    for (const [bufSeq] of reorderBuffer) {
                        if (bufSeq < firstKeyframeSeq) {
                            reorderBuffer.delete(bufSeq);
                        }
                    }
                    nextExpectedSequence = firstKeyframeSeq;
                    processBufferedChunks();
                } else {
                    warnLog?.log(`No keyframe in buffer after lost packet #${nextExpectedSequence}, entering recovery mode`);
                    waitingForKeyframe = true;
                    reorderBuffer.clear();
                }
                return;
            }

            if (chunkData.type === 'key') {
                debugLog?.log(`Received keyframe #${seq} while waiting for #${nextExpectedSequence}`);
                nextExpectedSequence = seq;
                decodeChunk(chunkData);
                nextExpectedSequence = seq + 1;
                for (const [bufSeq] of reorderBuffer) {
                    if (bufSeq < seq) {
                        reorderBuffer.delete(bufSeq);
                    }
                }
                processBufferedChunks();
                return;
            }

            processBufferedChunks();
            return;
        }

        // Process this chunk immediately (it's in order)
        decodeChunk(chunkData);

        if (seq !== -1) {
            nextExpectedSequence = seq + 1;
            processBufferedChunks();
        }
    },

    /**
     * Decode raw encoded bytes (used by video-player.ts for off-main-thread decoding).
     * Creates EncodedVideoChunk internally from raw bytes.
     */
    // eslint-disable-next-line
    decodeRawChunk: async (
        timestamp: number,
        duration: number,
        isKeyFrame: boolean,
        sequenceNumber: number,
        data: ArrayBuffer,
        description?: ArrayBuffer
    ): Promise<void> => {
        if (!decoder || !processing) {
            return;
        }

        try {
            // If we have a description and it's a keyframe, reconfigure the decoder only if description changed
            if (isKeyFrame && description && description.byteLength > 0) {
                if (!lastRawDescription || !bufferEqual(lastRawDescription, description)) {
                    decoder.updateDescription(description);
                    lastRawDescription = description.slice(0);
                    infoLog?.log('Description changed, decoder reconfigured');
                }
                decoderConfigured = true;
            } else if (isKeyFrame && !decoderConfigured && currentDecoderConfig?.description) {
                // Recreate decoder with description to avoid double-configure.
                // Handles skipTo jumping past the first keyframe with per-frame SPS/PPS.
                infoLog?.log('Recreating decoder with initial description for skipTo keyframe');
                if (decoder.getState() !== 'closed') {
                    decoder.close();
                }
                decoder = new WebCodecsDecoder(
                    currentDecoderConfig,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                    }
                );
                decoder.initialize();
                decoderConfigured = true;
            }

            // For AV1, we don't need a description — mark as configured on first keyframe
            if (isKeyFrame && !decoderConfigured) {
                const isAV1 = currentDecoderConfig?.codec.startsWith('av01');
                if (isAV1 || !currentDecoderConfig?.description) {
                    decoderConfigured = true;
                }
            }

            // Defense-in-depth: never feed empty data to the decoder
            if (data.byteLength === 0) {
                warnLog?.log(`Skipping chunk with empty data: seq=${sequenceNumber}, isKey=${isKeyFrame}`);
                return;
            }

            // Create EncodedVideoChunk from raw bytes
            const chunk = new EncodedVideoChunk({
                type: isKeyFrame ? 'key' : 'delta',
                timestamp,
                duration,
                data,
            });

            // Check decoder state — recover from closed/error state on keyframe
            if (decoder.getState() !== 'configured') {
                if (isKeyFrame && currentDecoderConfig) {
                    // HEVC/AVC require description on every configure() — recovery must re-apply
                    // the cached description, otherwise the next keyframe fails with
                    // "A key frame is required after configure()" DataError.
                    const recoveryDescription: ArrayBuffer | undefined = description && description.byteLength > 0
                        ? description
                        : (lastRawDescription ?? undefined);
                    warnLog?.log(`Decoder in state '${decoder.getState()}', recovering on keyframe (descLen=${recoveryDescription?.byteLength ?? 0})`);
                    try {
                        const recoveryConfig: DecoderConfig = recoveryDescription
                            ? { ...currentDecoderConfig, description: recoveryDescription }
                            : { ...currentDecoderConfig, description: undefined };
                        decoder = new WebCodecsDecoder(
                            recoveryConfig,
                            emitDecodedFrame,
                            (error) => {
                                errorLog?.log('Decoder error:', error);
                            }
                        );
                        decoder.initialize();
                        decoderConfigured = true;
                        if (recoveryDescription) {
                            lastRawDescription = recoveryDescription.slice(0);
                        }
                    } catch (recoveryError) {
                        errorLog?.log('Decoder recovery failed:', recoveryError);
                        return;
                    }
                } else {
                    // Can't recover on delta frame — need keyframe
                    return;
                }
            }

            if (isKeyFrame) {
                infoLog?.log(`Decoding keyframe: seq=${sequenceNumber}, state=${decoder.getState()}, ` +
                    `configured=${decoderConfigured}, descLen=${description?.byteLength ?? 0}, dataLen=${data.byteLength}`);
            }

            // Decode using the WebCodecsDecoder wrapper (tracks timing for diagnostics)
            decoder.decodeRaw(chunk);
        } catch (error) {
            errorLog?.log('Error decoding raw chunk:', error);
        }
    },

    /**
     * Reset the decoder (flush internal queue).
     * Used for tab visibility restore.
     */
    // eslint-disable-next-line
    resetDecoder: async (): Promise<void> => {
        if (!decoder) return;

        try {
            infoLog?.log('Resetting decoder');

            // Close existing decoder
            if (decoder.getState() !== 'closed') {
                decoder.close();
            }

            // Recreate decoder
            if (currentDecoderConfig) {
                decoder = createDecoder(currentDecoderConfig);
                decoder.initialize();
                decoderConfigured = false;
                infoLog?.log('Decoder reset complete');
            }
        } catch (error) {
            errorLog?.log('Error resetting decoder:', error);
        }
    },

    /**
     * Reconfigure the decoder with new config.
     * Used after reset for tab visibility restore.
     */
    // eslint-disable-next-line
    configureDecoder: async (config: DecoderConfig): Promise<void> => {
        try {
            infoLog?.log('Configuring decoder with:', config.codec);
            currentDecoderConfig = config;

            if (decoder && decoder.getState() !== 'closed') {
                decoder.close();
            }

            if (config.description) {
                // Single configure() in AVCC mode — same pattern as initialize().
                // Do NOT use createDecoder() which strips description, causing
                // double-configure (Annex B → AVCC) that breaks Chrome's VideoDecoder.
                decoder = new WebCodecsDecoder(
                    config,
                    emitDecodedFrame,
                    (error) => {
                        errorLog?.log('Decoder error:', error);
                    }
                );
                decoder.initialize();
                decoderConfigured = true;

                // Sync lastRawDescription so decodeRawChunk doesn't redundantly reconfigure
                const desc = config.description;
                if (desc instanceof ArrayBuffer) {
                    lastRawDescription = desc.slice(0);
                } else if (ArrayBuffer.isView(desc)) {
                    lastRawDescription = desc.buffer.slice(
                        desc.byteOffset, desc.byteOffset + desc.byteLength) as ArrayBuffer;
                }
            } else {
                decoder = createDecoder(config);
                decoder.initialize();
                decoderConfigured = false;
                lastRawDescription = null;
            }

            infoLog?.log('Decoder configured');
        } catch (error) {
            errorLog?.log('Error configuring decoder:', error);
            throw error;
        }
    },

    /**
   * Flush pending chunks
   */
    flush: async (): Promise<void> => {
        if (decoder) {
            try {
                await decoder.flush();
                infoLog?.log('Decoder flushed');
            } catch (error) {
                warnLog?.log('Decoder flush error:', error);
            }
        }
    },

    /**
   * Get current decoder statistics
   */
    // eslint-disable-next-line
    getStats: async (): Promise<DecoderStats> => {
        return decoder?.getStats() ?? {
            decodedFrames: 0,
            droppedFrames: 0,
            averageDecodeTime: 0,
            medianDecodeTime: 0,
            pureMedianDecodeTime: -1,
            decodeQueueSize: 0,
            backpressureDrops: 0,
            hardwareAcceleration: 'unknown',
            resolution: 'N/A'
        };
    },

    /**
   * Toggle between WASM and built-in decoders
   */
    // eslint-disable-next-line
    toggleDecoderType: async (useWasm: boolean): Promise<void> => {
        try {
            infoLog?.log('Toggling decoder type to', useWasm ? 'WASM' : 'built-in');

            if (!decoder) {
                throw new Error('Decoder not initialized');
            }

            infoLog?.log('WebCodecs decoder - using WebCodecs API');
        } catch (error) {
            errorLog?.log('Failed to toggle decoder type:', error);
            throw error;
        }
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    startPullInWorker: async (
        streamId: string,
        skipToMs: number,
        apiUrl: string,
        startedAtMs: number,
        jitterBufferMs: number,
        syncPort: MessagePort,
        writable?: WritableStream<VideoFrame>,
    ): Promise<void> => {
        if (mstgSelector) {
            warnLog?.log('startPullInWorker called while another selector is active — replacing');
            mstgSelector.dispose();
            mstgSelector = null;
        }

        let selectorWritable: WritableStream<VideoFrame>;
        if (writable) {
            // Tier 2: main constructed MSTG and already attached the track.
            selectorWritable = writable;
            infoLog?.log(`Off-thread renderer using main-supplied writable (tier 2), startedAtMs=${startedAtMs}, jitterBufferMs=${jitterBufferMs}`);
        } else {
            // Tier 1: try to construct generator inside this worker.
            const gen = tryCreateOffThreadGenerator();
            if (!gen) {
                try { syncPort.close(); } catch { /* ignore */ }
                throw new Error('Off-thread renderer unsupported: neither MediaStreamTrackGenerator nor VideoTrackGenerator is available in worker context');
            }
            selectorWritable = gen.writable;
            void callbacks.onOffThreadTrackReady(gen.track, rpcNoWait);
            infoLog?.log(`Off-thread renderer enabled in worker (tier 1, ${gen.api}), startedAtMs=${startedAtMs}, jitterBufferMs=${jitterBufferMs}`);
        }

        mstgSelector = new WorkerMstgSelector(selectorWritable, syncPort, startedAtMs, jitterBufferMs);

        // Lazy Api init (idempotent — second call no-ops with a warn log).
        if (!apiInitialized) {
            Api.init(apiUrl, streamingApi);
            Api.bindDotNetRpcConnected(WorkerConnectivityUI);
            Api.requireConnection('VideoDecoder');
            apiInitialized = true;
        }

        pullStartedAtMs = startedAtMs;
        pullActive = true;
        // Fire and forget — pull loop runs in background, ends via onPullEnded.
        void runPullLoop(streamId, skipToMs);
    },

    stopPullInWorker: async (): Promise<void> => {
        pullActive = false;
        if (pullAbortController) {
            pullAbortController.abort();
            pullAbortController = null;
        }
        await Promise.resolve();
    },

    // eslint-disable-next-line @typescript-eslint/require-await
    onConnectivityUpdate: async (
        isOnline: boolean,
        isConnected: boolean,
        isBlazorServer: boolean,
    ): Promise<void> => {
        WorkerConnectivityUI.update(isOnline, isConnected, isBlazorServer);
    }
};

// Two slightly different APIs produce equivalent (writable, MediaStreamTrack):
//   - MediaStreamTrackGenerator: Chromium (also exposed in workers). The
//     generator IS the MediaStreamTrack.
//   - VideoTrackGenerator: Safari worker-only. Has .track + .writable.
interface OffThreadGenerator {
    readonly track: MediaStreamTrack;
    readonly writable: WritableStream<VideoFrame>;
    readonly api: 'MediaStreamTrackGenerator' | 'VideoTrackGenerator';
}

function tryCreateOffThreadGenerator(): OffThreadGenerator | null {
    const g = globalThis as unknown as {
        MediaStreamTrackGenerator?: new (init: { kind: 'video' }) => MediaStreamTrack & { readonly writable: WritableStream<VideoFrame> };
        VideoTrackGenerator?: new () => { readonly track: MediaStreamTrack; readonly writable: WritableStream<VideoFrame> };
    };
    if (typeof g.MediaStreamTrackGenerator === 'function') {
        const generator = new g.MediaStreamTrackGenerator({ kind: 'video' });
        return { track: generator, writable: generator.writable, api: 'MediaStreamTrackGenerator' };
    }
    if (typeof g.VideoTrackGenerator === 'function') {
        const vtg = new g.VideoTrackGenerator();
        return { track: vtg.track, writable: vtg.writable, api: 'VideoTrackGenerator' };
    }
    return null;
}

// Initialize RPC communication (bidirectional)
const callbacks = rpcClientServer<DecoderWorkerCallbacks>(
    'DecoderWorker',
  self as unknown as Worker,
  serverImpl
);

infoLog?.log('Decoder worker initialized');
