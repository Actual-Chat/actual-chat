import { getLogs } from 'logging';
import { delayAsync, PromiseSource } from 'actuallab-core';
import { RpcStream } from 'actuallab-rpc';
import { Api, int64ToNumber, MediaRpcStreamOptions, toInt64, uploadsApi } from 'api';
import { ConnectivityUI } from '../ConnectivityUI/connectivity-ui';
import { IFileUpload, IUploadStreamSource } from './web-uploads';
import {
    FileUploadProgressReporter,
    OffsetConflictError,
    ProgressReporter,
    UploadNotFoundError,
    UploadTransientFailure,
    isAbortError,
    isConnectivityError,
    mapUploadError,
} from './chunked-file-upload';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('FileUpload');

// '~' = use the session bound to the current RPC connection (cookie/header
// resolved at WS handshake), matching audio-streamer.ts's RPC_SESSION_DEFAULT.
const RPC_SESSION_DEFAULT = '~';

// Wraps an async progress sink so callers can fire `(pct) => void` freely while
// at most one underlying call stays in flight: while one runs, the latest value
// is remembered and sent once it resolves (trailing-edge coalescing). Without
// this, one report per 16 KB sub-chunk floods the JS->.NET render channel and
// the progress bar lags far behind the actual upload.
function coalesceProgress(report: (progressPercent: number) => PromiseLike<unknown>): ProgressReporter {
    let inFlight = false;
    let pending: number | null = null;
    let lastSent = -1;

    const pump = async (): Promise<void> => {
        inFlight = true;
        try {
            while (pending !== null) {
                const pct = Math.trunc(pending);
                pending = null;
                if (pct === lastSent)
                    continue;
                lastSent = pct;
                await report(pct);
            }
        }
        finally {
            inFlight = false;
        }
    };

    return (progressPercent: number) => {
        pending = progressPercent;
        if (!inFlight)
            void pump();
    };
}

/**
 * Streaming upload — pushes file data as an RpcStream<byte[]> of small
 * sub-chunks to IUploads.AppendStream instead of one large OnAppend per chunk.
 * Small stream items let the serialized WS writer yield between sends so
 * keep-alive can interleave on slow links (no head-of-line blocking).
 */
export class StreamFileUpload implements IFileUpload {
    private static readonly SubChunkSize = 16 * 1024; // 16 KB per RpcStream item
    private static readonly ReadBlockSize = 1024 * 1024; // read the blob 1 MB at a time, then slice
    private readonly whenCompletedSource: PromiseSource<void> = new PromiseSource<void>();
    private readonly abortController: AbortController = new AbortController();
    private readonly connectionScope: string;

    constructor(
        private readonly uploadId: string,
        private readonly blob: Blob,
        private readonly progressReporter: ProgressReporter)
    {
        this.connectionScope = `FileUpload:${uploadId}`;
    }

    public get whenCompleted(): Promise<void> {
        return this.whenCompletedSource;
    }

    public static startWithReporter(
        uploadId: string,
        source: IUploadStreamSource,
        blazorRef: DotNet.DotNetObject,
        onCompleted?: () => void,
    ): StreamFileUpload {
        const blob = source.getBlob();
        const reporter = new FileUploadProgressReporter(blazorRef);
        const upload = new StreamFileUpload(uploadId, blob, coalesceProgress(pct => reporter.reportProgress(pct)));
        upload.whenCompleted.then(() => {
            void reporter.reportUploadSucceed();
        }).catch((e: unknown) => {
            if (isAbortError(e)) {
                debugLog?.log(`File upload '${uploadId}' cancelled`);
                void reporter.reportUploadCancelled();
            } else if (e instanceof UploadNotFoundError) {
                errorLog?.log(`File upload '${uploadId}' not found`);
                void reporter.reportUploadNotFound();
            } else {
                errorLog?.log('Failed to upload file', e);
                void reporter.reportUploadFailed();
            }
        }).finally(() => {
            onCompleted?.();
        });
        upload.start();
        return upload;
    }

    public start() {
        void this.startInternal();
    }

    public cancel() {
        this.abortController.abort();
    }

    private async startInternal() {
        infoLog?.log(`startInternal (stream): id=${this.uploadId} size=${this.blob.size}`);
        Api.requireConnection(this.connectionScope);
        let retryIndex = 0;
        const maxRetries = 3;
        let run = true;
        try {
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            while (run) {
                run = false;
                try {
                    const fileSize = this.blob.size;
                    const offset = await this.getOffset();
                    debugLog?.log(`Starting stream upload of ${this.uploadId} at offset ${offset}`);
                    this.progressReporter((fileSize > 0 ? offset / fileSize : 1) * 100);
                    if (offset >= fileSize) {
                        this.whenCompletedSource.resolve();
                        return;
                    }

                    const newOffset = await this.appendStream(offset);
                    if (newOffset < fileSize) {
                        // Partial upload — re-query the offset and continue streaming.
                        run = true;
                        continue;
                    }
                    this.progressReporter(100);
                    this.whenCompletedSource.resolve();
                    return;
                } catch (error) {
                    if (this.isCancelled()) {
                        this.whenCompletedSource.reject(this.abortController.signal.reason);
                        return;
                    }
                    const mapped = mapUploadError(error);
                    if ((mapped instanceof OffsetConflictError || mapped instanceof UploadTransientFailure)
                        && retryIndex < maxRetries) {
                        warnLog?.log('Streaming upload failure. Retrying...', mapped);
                        retryIndex++;
                        await delayAsync(500);
                        run = true;
                        continue;
                    }
                    if (isConnectivityError(mapped) && retryIndex < maxRetries) {
                        retryIndex++;
                        run = true;
                        await ConnectivityUI.whenConnected();
                        continue;
                    }
                    this.whenCompletedSource.reject(mapped);
                    return;
                }
            }
        }
        finally {
            Api.releaseConnection(this.connectionScope);
        }
    }

    private async getOffset(): Promise<number> {
        try {
            const result = await uploadsApi.uploads.GetOffset(RPC_SESSION_DEFAULT, this.uploadId);
            return int64ToNumber(result);
        } catch (e: unknown) {
            throw mapUploadError(e);
        }
    }

    private async appendStream(offset: number): Promise<number> {
        const peer = Api.peer;
        const stream = new RpcStream<Uint8Array>(
            signal => this.readSubChunks(offset, signal),
            MediaRpcStreamOptions.upload<Uint8Array>());
        debugLog?.log(`-> AppendStream(${this.uploadId}) offset=${offset}`);
        const t0 = performance.now();
        try {
            const result = await uploadsApi.uploads.AppendStream(
                RPC_SESSION_DEFAULT, this.uploadId, toInt64(offset), stream.toRef(peer));
            const newOffset = int64ToNumber(result);
            debugLog?.log(`<- AppendStream(${this.uploadId}) = ${newOffset} in ${(performance.now() - t0).toFixed(0)}ms`);
            return newOffset;
        } catch (e: unknown) {
            warnLog?.log(`<- AppendStream(${this.uploadId}) threw after ${(performance.now() - t0).toFixed(0)}ms:`, e);
            throw mapUploadError(e);
        } finally {
            stream.disconnect();
        }
    }

    // Read the blob a block at a time and hand out 16 KB sub-chunks as zero-copy
    // views into it (instead of one async blob read per sub-chunk): ~64x fewer
    // reads and allocations. subarray views are safe here — msgpack encodes a
    // Uint8Array by its byteOffset/byteLength and copies on send, and the block
    // is never mutated.
    private async *readSubChunks(startOffset: number, signal: AbortSignal): AsyncIterable<Uint8Array> {
        const size = this.blob.size;
        let pos = startOffset;
        while (pos < size) {
            signal.throwIfAborted();
            const blockEnd = Math.min(pos + StreamFileUpload.ReadBlockSize, size);
            const block = new Uint8Array(await this.blob.slice(pos, blockEnd).arrayBuffer());
            for (let off = 0; off < block.length; off += StreamFileUpload.SubChunkSize) {
                this.abortController.signal.throwIfAborted();
                const subEnd = Math.min(off + StreamFileUpload.SubChunkSize, block.length);
                const sub = block.subarray(off, subEnd);
                pos += sub.length;
                yield sub;

                this.progressReporter((pos / size) * 100);
            }
        }
    }

    private isCancelled(): boolean {
        return this.abortController.signal.aborted;
    }
}
