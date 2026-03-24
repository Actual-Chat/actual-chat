/**
 * Stream Channel utilities for transferring ReadableStream/WritableStream to Web Workers.
 * Replaces per-frame RPC postMessage with zero-overhead stream piping.
 */

// --- Shared message types for stream-based data transfer ---

/**
 * Serialized encoded chunk data flowing from encoder worker to main thread via stream.
 */
export interface SerializedChunkMessage {
    chunkBytes: ArrayBuffer;
    timestamp: number;
    duration: number;
    isKeyFrame: boolean;
    codec: string;
    sequenceNumber: number;
    descriptionBytes?: ArrayBuffer;
}

/**
 * Raw encoded chunk data flowing from main thread to decoder worker via stream.
 */
export interface RawChunkMessage {
    timestamp: number;      // microseconds
    duration: number;       // microseconds
    isKeyFrame: boolean;
    sequenceNumber: number;
    data: ArrayBuffer;
    description?: ArrayBuffer;
}

// --- Feature detection ---

let _supportsTransferableStreams: boolean | null = null;

/**
 * Detect whether the current browser supports transferring ReadableStream/WritableStream
 * via postMessage (structured clone with transfer list).
 * Supported: Chrome 87+, Firefox 102+, Safari 16.4+.
 * Result is cached after first call.
 */
export function supportsTransferableStreams(): boolean {
    if (_supportsTransferableStreams !== null)
        return _supportsTransferableStreams;

    try {
        const ts = new TransformStream();
        const mc = new MessageChannel();
        mc.port1.postMessage(ts.readable, [ts.readable]);
        mc.port1.close();
        mc.port2.close();
        _supportsTransferableStreams = true;
    } catch {
        _supportsTransferableStreams = false;
    }
    return _supportsTransferableStreams;
}

// --- Stream channel creation ---

/**
 * A unidirectional stream channel: one side writes, the other reads.
 * The writable/readable ends are meant to be transferred to a worker.
 */
export interface StreamEndpoints<T> {
    /** Writer for the sender side */
    writer: WritableStreamDefaultWriter<T>;
    /** Readable stream to transfer to the consumer (worker) */
    readable: ReadableStream<T>;
}

/**
 * Create a unidirectional stream channel with a TransformStream.
 * The caller gets a writer; the readable end is meant to be transferred to a worker.
 *
 * @param highWaterMark Queue depth before backpressure kicks in.
 *   Use low values (1-2) for VideoFrame streams (GPU resources have limited lifetime).
 *   Use higher values for encoded data streams.
 */
export function createInputChannel<T>(highWaterMark = 1): StreamEndpoints<T> {
    const ts = new TransformStream<T, T>(undefined, undefined, { highWaterMark });
    return {
        writer: ts.writable.getWriter(),
        readable: ts.readable,
    };
}

