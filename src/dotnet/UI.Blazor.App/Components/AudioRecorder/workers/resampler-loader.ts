import resamplerModuleFactory, { ResamplerModule, Resampler } from '@actual-chat/resampler';
import ResamplerWasm from '@actual-chat/resampler/resampler.wasm';
import { Versioning } from 'versioning';
import { retry } from 'promises';

export class ResamplerLoader {
    private static resamplerModule: ResamplerModule = null;
    private resampler: Resampler = null;
    private fromSr: number = 0;
    private toSr: number = 0;

    public whenResamplerReady: Promise<void> = null;
    public load(): Promise<void> {
        if (ResamplerLoader.resamplerModule) {
            return Promise.resolve();
        }
        if (this.whenResamplerReady) {
            return this.whenResamplerReady;
        }
        this.whenResamplerReady =  (async () => {
            ResamplerLoader.resamplerModule ??= await retry(3, () => resamplerModuleFactory(getResamplerEmscriptenLoaderOptions()));
        })();
        return this.whenResamplerReady;
    }

    public async getResampler(fromSampleRate: number, toSampleRate: number): Promise<Resampler> {
        if (!ResamplerLoader.resamplerModule) {
            await this.load();
        }

        if (this.resampler && this.fromSr === fromSampleRate && this.toSr === toSampleRate)
            return this.resampler;

        if (this.resampler)
            this.resampler.delete();
        return this.createResampler(fromSampleRate, toSampleRate);
    }

    private createResampler(fromSampleRate: number, toSampleRate: number): Resampler {
        // @ts-ignore
        this.resampler = new ResamplerLoader.resamplerModule.Resampler(fromSampleRate, toSampleRate);
        this.fromSr = fromSampleRate;
        this.toSr = toSampleRate;
        return this.resampler;
    }
}


function getResamplerEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(ResamplerWasm);
            if (filename.slice(-4) === 'wasm')
                return codecWasmPath;

                // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.slice(0, 5) === 'data:')
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}
