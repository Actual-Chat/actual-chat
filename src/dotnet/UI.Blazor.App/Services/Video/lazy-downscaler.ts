import type { DownscalerLike, LayerSpec } from './operators/downscale';

// Defers underlying downscaler init until the first `process()` call —
// the recorder builds the pipeline cheaply and only pays the device init
// cost (WebGPU adapter init, GL context creation, etc.) when frames
// actually start flowing.
//
// Factory is invoked exactly once; if it throws, the input frame is
// closed and the exception is rethrown. Subsequent dispose() resets
// the wrapper, allowing the next process() call to re-invoke the factory.
export class LazyDownscaler implements DownscalerLike {
    private inner: DownscalerLike | null = null;
    private initPromise: Promise<DownscalerLike> | null = null;

    constructor(private readonly factory: () => Promise<DownscalerLike>) {}

    async process(input: VideoFrame, layers: readonly LayerSpec[]): Promise<VideoFrame[]> {
        let inner = this.inner;
        if (!inner) {
            this.initPromise ??= this.factory();
            try {
                inner = await this.initPromise;
                this.inner = inner;
            } catch (e) {
                try { input.close(); } catch { /* already closed */ }
                throw e;
            }
        }
        return inner.process(input, layers);
    }

    dispose(): void {
        try { this.inner?.dispose?.(); } catch { /* ignore */ }
        this.inner = null;
        this.initPromise = null;
    }
}
