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
    private static _current: WorkerClient | null = null;

    /** A string source is fetched here rather than in the worker: WebViews handle
     *  custom-scheme requests from workers inconsistently. */
    public static async process(source: Blob | string, request: ImageProcessRequest): Promise<ImageProcessResult> {
        const blob = typeof source === 'string' ? await fetchBlob(source) : source;
        const current = this.getClient();
        // The worker runs jobs one at a time, so a job's deadline includes the jobs queued ahead of
        // it - on this worker. Jobs abandoned on a terminated one must not charge the replacement.
        const timeout: RpcTimeout = { type: 'rpc-timeout', timeoutMs: PROCESS_TIMEOUT_MS * (current.pendingCount + 1) };
        current.pendingCount++;
        try {
            return await current.client.process(blob, request, timeout);
        }
        catch (e) {
            // A worker killed for memory (e.g. decoding a huge photo) never raises `error` in
            // Chromium/WebKit - the pending call just times out, so that's the crash signal here.
            // Only reset for the client this call was made on: a stale timeout from a job queued
            // on an already-replaced worker must not tear down the healthy replacement.
            if (e instanceof TimeoutError && this._current === current)
                this.reset();

            throw e;
        }
        finally {
            current.pendingCount--;
        }
    }

    // Private methods

    private static getClient(): WorkerClient {
        if (this._current)
            return this._current;

        const worker = new Worker(Versioning.mapPath('/dist/imageProcessorWorker.js'), { type: 'module' });
        worker.onerror = (e: ErrorEvent) => {
            errorLog?.log('worker error, recreating on next use:', e);
            this.reset();
        };
        const client = rpcClient<ImageProcessorWorker>('ImageProcessor.client', worker, PROCESS_TIMEOUT_MS);
        client.init(new URL('/dist/jpegli', globalThis.location.href).href)
            .catch((e: unknown) => errorLog?.log('init failed:', e));
        this._current = { worker, client, pendingCount: 0 };
        return this._current;
    }

    private static reset(): void {
        this._current?.client.dispose();
        this._current?.worker.terminate();
        this._current = null;
    }
}

interface WorkerClient {
    readonly worker: Worker;
    readonly client: ImageProcessorWorker & Disposable;
    pendingCount: number;
}

async function fetchBlob(url: string): Promise<Blob> {
    const response = await fetch(url);
    if (!response.ok)
        throw new Error(`ImageProcessor: HTTP ${response.status} while fetching '${url}'.`);

    return await response.blob();
}
