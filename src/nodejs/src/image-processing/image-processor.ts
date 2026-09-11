import { rpcClient, RpcTimeout } from 'rpc';
import { Disposable } from 'disposable';
import { getLogs } from 'logging';
import { Versioning } from 'versioning';
import { TimeoutError } from 'actuallab-core';
import type { ImageProcessorWorker, ImageProcessRequest, ImageProcessResult } from './image-processing-contracts';

const { errorLog } = getLogs('ImageProcessor');

const PROCESS_TIMEOUT_MS = 30_000;

/** Resizes, re-encodes (jpegli) or strips metadata of images in a module worker, one image at a time. */
export class ImageProcessor {
    private static _worker: Worker | null = null;
    private static _client: (ImageProcessorWorker & Disposable) | null = null;
    private static _pendingCount = 0;

    /** A string source is fetched here rather than in the worker: WebViews handle
     *  custom-scheme requests from workers inconsistently. */
    public static async process(source: Blob | string, request: ImageProcessRequest): Promise<ImageProcessResult> {
        const blob = typeof source === 'string' ? await fetchBlob(source) : source;
        // The worker runs jobs one at a time, so a job's deadline includes the jobs queued ahead of it
        const timeout: RpcTimeout = { type: 'rpc-timeout', timeoutMs: PROCESS_TIMEOUT_MS * (this._pendingCount + 1) };
        const client = this.getClient();
        this._pendingCount++;
        try {
            return await client.process(blob, request, timeout);
        }
        catch (e) {
            // A worker killed for memory (e.g. decoding a huge photo) never raises `error` in
            // Chromium/WebKit - the pending call just times out, so that's the crash signal here.
            // Only reset for the client this call was made on: a stale timeout from a job queued
            // on an already-replaced worker must not tear down the healthy replacement.
            if (e instanceof TimeoutError && this._client === client)
                this.reset();

            throw e;
        }
        finally {
            this._pendingCount--;
        }
    }

    // Private methods

    private static getClient(): ImageProcessorWorker & Disposable {
        if (this._client)
            return this._client;

        const worker = new Worker(Versioning.mapPath('/dist/imageProcessorWorker.js'), { type: 'module' });
        worker.onerror = (e: ErrorEvent) => {
            errorLog?.log('worker error, recreating on next use:', e);
            this.reset();
        };
        const client = rpcClient<ImageProcessorWorker>('ImageProcessor.client', worker, PROCESS_TIMEOUT_MS);
        client.init(new URL('/dist/jpegli', globalThis.location.href).href)
            .catch((e: unknown) => errorLog?.log('init failed:', e));
        this._worker = worker;
        this._client = client;
        return client;
    }

    private static reset(): void {
        this._client?.dispose();
        this._worker?.terminate();
        this._client = null;
        this._worker = null;
    }
}

async function fetchBlob(url: string): Promise<Blob> {
    const response = await fetch(url);
    if (!response.ok)
        throw new Error(`ImageProcessor: HTTP ${response.status} while fetching '${url}'.`);

    return await response.blob();
}
