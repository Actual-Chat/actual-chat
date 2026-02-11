import { Log } from 'logging';
import { delayAsync, OperationCancelledError, PromiseSource } from 'promises';
import { BrowserInit } from '../../Services/BrowserInit/browser-init';
import { SessionTokens } from '../../Services/Security/session-tokens';
import { Connectivity } from 'connectivity';

const { debugLog, warnLog, errorLog } = Log.get('FileUpload');

type ProgressReporter = (progressPercent: number) => void;

export class FileUploadProgressReporter {
    constructor(private blazorRef: DotNet.DotNetObject)
    {
    }

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

export class ChunkedFileUpload {
    private readonly whenCompletedSource: PromiseSource<void> = new PromiseSource<void>();
    private readonly uploadUrl: string;
    private readonly abortController: AbortController = new AbortController();

    constructor(
        private readonly uploadId: string,
        private readonly blob: Blob,
        private readonly progressReporter: ProgressReporter)
    {
        this.uploadUrl = BrowserInit.getUrl(`api/uploads/${this.uploadId}`);
    }

    public get whenCompleted(): Promise<void> {
        return this.whenCompletedSource;
    }

    public static startWithReporter(
        uploadId: string,
        blob: Blob,
        blazorRef: DotNet.DotNetObject,
        onCompleted?: () => void,
    ): ChunkedFileUpload {
        const reporter = new FileUploadProgressReporter(blazorRef);
        const upload = new ChunkedFileUpload(uploadId, blob, pct => void reporter.reportProgress(pct));
        upload.whenCompleted.then(() => {
            void reporter.reportUploadSucceed();
        }).catch((e: unknown) => {
            if (e instanceof OperationCancelledError) {
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
        let retryIndex = 0;
        const maxRetries = 3;
        let run = true;
        const chunkSizeSelector = new ChunkSizeSelector();
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
                chunkSizeSelector.adaptOnUploadIssue(error instanceof TypeError);
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
                else if (error instanceof TypeError) {
                    // Network-level error (no connection, timeout, offline, etc.)
                    if (retryIndex < maxRetries) {
                        retryIndex++;
                        run = true;
                        await Connectivity.whenOnline();
                        continue;
                    }
                }
                if (this.isCancelled()) {
                    this.whenCompletedSource.reject(new OperationCancelledError('File upload cancelled'));
                } else {
                    this.whenCompletedSource.reject(error);
                }
                return;
            }
        }
    }

    private async getOffset(): Promise<number>
    {
        const response = await fetch(this.uploadUrl, {
            method: 'HEAD',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current,
                'Tus-Resumable' : '1.0.0',
            },
            signal: this.abortController.signal
        });
        if (!response.ok) {
            if (response.status == 404)
                throw new UploadNotFoundError(`Upload ${this.uploadId} not found`);
            if (response.status == 503)
                throw new UploadTransientFailure(`Upload transient failure`);
            throw new Error(`Failed to get upload status: ${response.statusText}`);
        }
        const header = response.headers.get('Upload-Offset');
        if (!header)
            throw new Error('Upload-Offset header not found in response');
        return parseInt(header, 10);
    }

    private async uploadChunk(offset: number, chunkSize: number): Promise<number>
    {
        const contentType = 'application/offset+octet-stream';
        const expectedNewOffset = offset + chunkSize;
        const chunk = this.blob.slice(offset, expectedNewOffset, contentType);
        const response = await fetch(this.uploadUrl, {
            method: 'PATCH',
            headers: {
                [SessionTokens.headerName]: SessionTokens.current,
                'Content-Type': contentType,
                'Upload-Offset': offset.toString(),
                'Tus-Resumable' : '1.0.0',
            },
            body: chunk,
            signal: this.abortController.signal
        });

        if (!response.ok) {
            if (response.status == 404)
                throw new UploadNotFoundError(`Upload ${this.uploadId} not found`);
            if (response.status == 503)
                throw new UploadTransientFailure(`Upload transient failure`);
            if (response.status == 409)
                throw new OffsetConflictError('Upload offset conflict');
            throw new Error(`Failed to upload chunk: ${response.statusText}`);
        }

        const newOffsetHeader = response.headers.get('Upload-Offset');
        if (!newOffsetHeader)
            throw new Error('Upload-Offset header not found in response');
        return parseInt(newOffsetHeader, 10);
    }

    private isCancelled() : boolean {
        return this.abortController.signal.aborted;
    }
}

interface Stat { multiplier: number; ms: number; }

class ChunkSizeSelector
{
    private static minChunkSize = 256 * 1024; // 256 KB
    private static defaultChunkSizeMultiplier = 8; // 4 Mb
    private static maxChunkSizeMultiplier = 16; // 8 Mb
    private static recommendedChunkSizeMultiplier = 8; // 4 Mb
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
        return typeof performance !== 'undefined' ? performance.now() : Date.now();
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

class UploadNotFoundError extends Error {
}

class UploadTransientFailure extends Error {
}

class OffsetConflictError extends Error {
}
