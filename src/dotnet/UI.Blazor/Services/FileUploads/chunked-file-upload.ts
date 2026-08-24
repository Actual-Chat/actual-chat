import { getLogs } from 'logging';
import { delayAsync, PromiseSource } from 'actuallab-core';
import { Api, int64ToNumber, toInt64, uploadsApi } from 'api';
import { ConnectivityUI } from '../ConnectivityUI/connectivity-ui';
import { IFileUpload, IUploadStreamSource } from './web-uploads';

const { debugLog, infoLog, warnLog, errorLog } = getLogs('FileUpload');

// '~' = use the session bound to the current RPC connection (cookie/header
// resolved at WS handshake), matching audio-streamer.ts's RPC_SESSION_DEFAULT.
const RPC_SESSION_DEFAULT = '~';

export type ProgressReporter = (progressPercent: number) => void;

export class FileUploadProgressReporter {
    constructor(private blazorRef: DotNet.DotNetObject)
    { }

    public async reportProgress(progressPercent: number) {
        return this.blazorRef.invokeMethodAsync('OnUploadProgress', Math.trunc(progressPercent));
    }

    public async reportUploadSucceed() {
        return this.blazorRef.invokeMethodAsync('OnUploadSucceed');
    }

    public async reportUploadFailed() {
        return this.blazorRef.invokeMethodAsync('OnUploadFailed');
    }

    public async reportUploadCancelled() {
        return this.blazorRef.invokeMethodAsync('OnUploadCancelled');
    }

    public async reportUploadNotFound() {
        return this.blazorRef.invokeMethodAsync('OnUploadNotFound');
    }
}

export class ChunkedFileUpload implements IFileUpload {
    private readonly whenCompletedSource: PromiseSource<void> = new PromiseSource<void>();
    private readonly abortController: AbortController = new AbortController();

    private readonly connectionScope: string;

    constructor(
        private readonly uploadId: string,
        private readonly blob: Blob,
        private readonly progressReporter: ProgressReporter)
    {
        // Per-upload connection scope: the peer's reconnect loop only opens the
        // WS while at least one scope is held. Released in startInternal()'s
        // finally block.
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
    ): ChunkedFileUpload {
        const blob = source.getBlob();
        const reporter = new FileUploadProgressReporter(blazorRef);
        const upload = new ChunkedFileUpload(uploadId, blob, pct => void reporter.reportProgress(pct));
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

    public start()
    {
        void this.startInternal();
    }

    public cancel() {
        this.abortController.abort();
    }

    private async startInternal() {
        infoLog?.log(`startInternal: id=${this.uploadId} size=${this.blob.size} ` +
            `Api.canConnect=${Api.canConnect} requiresConnection=${Api.requiresConnection} ` +
            `isDotNetRpcConnected=${Api.isDotNetRpcConnected}`);
        Api.requireConnection(this.connectionScope);
        let retryIndex = 0;
        const maxRetries = 3;
        let run = true;
        const chunkSizeSelector = new ChunkSizeSelector();
        try {
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            while (run) {
                run = false;
                try {
                    let offset = await this.getOffset();
                    debugLog?.log(`Starting upload of ${this.uploadId} at offset ${offset}`);
                    const fileSize = this.blob.size;
                    this.progressReporter((offset / fileSize) * 100);
                    // Upload chunks
                    while (offset < fileSize) {
                        this.abortController.signal.throwIfAborted();

                        const remainingBytes = fileSize - offset;
                        const chunkSize = chunkSizeSelector.getChunkSize();
                        const currentChunkSize = Math.min(chunkSize, remainingBytes);
                        const t0 = chunkSizeSelector.getTimestamp();
                        const newOffset = await this.uploadChunk(offset, currentChunkSize);
                        const dt = chunkSizeSelector.getElapsedTime(t0);
                        const expectedNewOffset = offset + currentChunkSize;
                        if (newOffset !== expectedNewOffset)
                            warnLog?.log(`Offset mismatch detected: ${expectedNewOffset} != ${newOffset}`);
                        offset = newOffset;
                        // Reset retry counter on successful chunk upload
                        retryIndex = 0;
                        chunkSizeSelector.adaptChunkSizeOnSucceedUpload(dt);
                        this.progressReporter((offset / fileSize) * 100);
                    }
                    chunkSizeSelector.updateRecommendation();
                    this.whenCompletedSource.resolve();
                    return;
                } catch (error) {
                    chunkSizeSelector.adaptOnUploadIssue(isConnectivityError(error));
                    if (error instanceof OffsetConflictError) {
                        if (retryIndex < maxRetries) {
                            warnLog?.log('Offset conflict detected. Retrying...');
                            retryIndex++;
                            run = true;
                            continue;
                        }
                    }
                    else if (error instanceof UploadTransientFailure) {
                        if (retryIndex < maxRetries) {
                            warnLog?.log('Upload transient failure. Retrying...');
                            await delayAsync(500);
                            retryIndex++;
                            // on transient server-side problems try to reduce chunk size for stability
                            run = true;
                            continue;
                        }
                    }
                    else if (isConnectivityError(error)) {
                        if (retryIndex < maxRetries) {
                            retryIndex++;
                            run = true;
                            await ConnectivityUI.whenConnected();
                            continue;
                        }
                    }
                    if (this.isCancelled()) {
                        this.whenCompletedSource.reject(this.abortController.signal.reason);
                    } else {
                        this.whenCompletedSource.reject(error);
                    }
                    return;
                }
            }
        }
        finally {
            Api.releaseConnection(this.connectionScope);
        }
    }

    private async getOffset(): Promise<number> {
        debugLog?.log(`-> GetOffset(${this.uploadId})`);
        const t0 = performance.now();
        try {
            const result = await uploadsApi.uploads.GetOffset(RPC_SESSION_DEFAULT, this.uploadId);
            debugLog?.log(`<- GetOffset(${this.uploadId}) = ${result} in ${(performance.now() - t0).toFixed(0)}ms`);
            return int64ToNumber(result);
        } catch (e: unknown) {
            warnLog?.log(`<- GetOffset(${this.uploadId}) threw after ${(performance.now() - t0).toFixed(0)}ms:`, e);
            throw mapUploadError(e);
        }
    }

    private async uploadChunk(offset: number, chunkSize: number): Promise<number> {
        const expectedNewOffset = offset + chunkSize;
        const arrayBuffer = await this.blob.slice(offset, expectedNewOffset).arrayBuffer();
        debugLog?.log(`-> OnAppend(${this.uploadId}) offset=${offset} size=${chunkSize}`);
        const t0 = performance.now();
        try {
            const result = await uploadsApi.uploads.OnAppend({
                Session: RPC_SESSION_DEFAULT,
                UploadId: this.uploadId,
                Offset: toInt64(offset),
                Chunk: new Uint8Array(arrayBuffer),
            });
            debugLog?.log(`<- OnAppend(${this.uploadId}) = ${result} in ${(performance.now() - t0).toFixed(0)}ms`);
            return int64ToNumber(result);
        } catch (e: unknown) {
            warnLog?.log(`<- OnAppend(${this.uploadId}) threw after ${(performance.now() - t0).toFixed(0)}ms:`, e);
            throw mapUploadError(e);
        }
    }

    private isCancelled() : boolean {
        return this.abortController.signal.aborted;
    }
}

/** Maps a server-side upload exception (carried via RPC error TypeName) to the
 *  matching JS error class. Falls through with the original error otherwise. */
export function mapUploadError(e: unknown): unknown {
    if (e instanceof Error) {
        const typeName = (e as { typeName?: string }).typeName;
        if (typeName === 'ActualChat.UploadNotFoundException')
            return new UploadNotFoundError(e.message);
        if (typeName === 'ActualChat.OffsetConflictException')
            return new OffsetConflictError(e.message);
        if (typeName === 'ActualChat.UploadTransientException')
            return new UploadTransientFailure(e.message);
    }
    return e;
}

/** Connectivity / disposed-peer style failures that should trigger a reconnect-and-retry. */
export function isConnectivityError(e: unknown): boolean {
    if (e instanceof OffsetConflictError || e instanceof UploadNotFoundError || e instanceof UploadTransientFailure)
        return false;
    if (isAbortError(e))
        return false;
    return e instanceof Error;
}

export function isAbortError(e: unknown): boolean {
    return e instanceof DOMException && e.name === 'AbortError';
}

interface Stat { multiplier: number; ms: number; }

class ChunkSizeSelector
{
    private static minChunkSize = 256 * 1024; // 256 KB
    private static defaultChunkSizeMultiplier = 8; // 2 Mb
    private static maxChunkSizeMultiplier = 16; // 4 Mb
    private static recommendedChunkSizeMultiplier = 8; // 2 Mb
    private static maxChunkUploadDurationMs = 5000; // 5 seconds

    private currentMultiplier: number;
    private lastStats: Stat[] = [];

    constructor(){
        this.currentMultiplier = ChunkSizeSelector.recommendedChunkSizeMultiplier;
    }

    public adaptChunkSizeOnSucceedUpload(duration : number)
    {
        // track stats (limit to last 5)
        this.lastStats.push({ multiplier: this.currentMultiplier, ms: duration });
        if (this.lastStats.length > 5)
            this.lastStats.shift();

        const isSlowUpload = duration > ChunkSizeSelector.maxChunkUploadDurationMs;
        if (isSlowUpload) {
            // Slow upload
            if (this.currentMultiplier === 1)
                return;
        }
        else {
            // Fast upload
            if (this.currentMultiplier === ChunkSizeSelector.maxChunkSizeMultiplier)
                return;
        }

        let averagePerf = 0.5;
        if (this.lastStats.length > 1) {
            const minWeight = 0.2; // 20%
            const lastIndex = this.lastStats.length - 1;
            let step = 0;
            let totalPerf = 0;
            let totalWeights = 0;
            for (let i = lastIndex; i >= 0; i--) {
                const stat = this.lastStats[i];
                const perf = stat.multiplier / stat.ms;
                const z = step / lastIndex;
                const weight = Math.pow(minWeight, z);
                totalPerf += perf * weight;
                totalWeights += weight;
                step++;
            }
            averagePerf = totalPerf / totalWeights;
        }
        else {
            averagePerf = this.lastStats[0].multiplier / this.lastStats[0].ms;
        }

        const averageMultiplier = averagePerf * ChunkSizeSelector.maxChunkUploadDurationMs;
        let newMultiplier = Math.round(averageMultiplier);
        if (isNaN(newMultiplier))
            newMultiplier = 8;
        else
            newMultiplier = Math.min(Math.max(newMultiplier, 1), ChunkSizeSelector.maxChunkSizeMultiplier);
        this.currentMultiplier = newMultiplier;
        debugLog?.log(`Adapted chunkSizeMultiplier=${this.currentMultiplier}`);
    }

    public getChunkSize() : number {
        return this.currentMultiplier * ChunkSizeSelector.minChunkSize;
    }

    public adaptOnUploadIssue(isConnectionIssue: boolean) {
        if (this.currentMultiplier === 1)
            return;

        if (isConnectionIssue && this.currentMultiplier > ChunkSizeSelector.defaultChunkSizeMultiplier)
            this.currentMultiplier = ChunkSizeSelector.defaultChunkSizeMultiplier;
        else
            this.currentMultiplier--;
        debugLog?.log(`Adapted chunkSizeMultiplier=${this.currentMultiplier} on error`);
    }

    public getTimestamp() : number {
        return performance.now();
    }

    public getElapsedTime(timestamp: number) : number {
        const now = this.getTimestamp();
        return Math.max(0, now - timestamp);
    }

    public updateRecommendation()
    {
        ChunkSizeSelector.recommendedChunkSizeMultiplier = this.currentMultiplier;
        debugLog?.log(`Set recommendedChunkSizeMultiplier=${this.currentMultiplier}`);
    }
}

export class UploadNotFoundError extends Error {
}

export class UploadTransientFailure extends Error {
}

export class OffsetConflictError extends Error {
}
