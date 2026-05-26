// TODO: remove eslint-disables and fix errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition,@typescript-eslint/no-misused-promises */
import resamplerModuleFactory, { ResamplerModule, Resampler } from '@actual-chat/resampler';
import ResamplerWasm from '@actual-chat/resampler/resampler.wasm';
import { Versioning } from 'versioning';
import { retry } from 'actuallab-core';
import { AudioRingBuffer } from '../audio-ring-buffer';

export class ResamplerWrapper implements Resampler {
    private ringBuffer: AudioRingBuffer = new AudioRingBuffer(8192, 1);
    constructor(private resampler: Resampler)
    { }

    resample(buffer: BufferSource, output?: Float32Array): Float32Array {
        const ringBuffer = this.ringBuffer;
        const expectedSamples = Math.floor(this.outSampleRate * buffer.byteLength / 4 / this.inSampleRate);
        const result = this.resampler.resample(buffer);

        this.ringBuffer.push([result]);
        if (this.ringBuffer.samplesAvailable >= expectedSamples) {
            if (output?.length === expectedSamples) {
                ringBuffer.pull([output]);
                return output;
            }
            const resampled = new Float32Array(expectedSamples);
            ringBuffer.pull([resampled]);
            return resampled;
        }
        return new Float32Array(0);
    }
    reset(): void {
        this.resampler.reset();
    }
    delete(): void {
        this.resampler.delete();
    }

    get inSampleRate(): number {
        return this.resampler.inSampleRate;
    }

    get outSampleRate(): number {
        return this.resampler.outSampleRate;
    }
}

export class ResamplerLoader {
    private static resamplerModule: ResamplerModule;
    private resampler: ResamplerWrapper;

    public whenResamplerReady: Promise<void>;
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

    public async getResampler(fromSampleRate: number, toSampleRate: number): Promise<ResamplerWrapper> {
        if (!ResamplerLoader.resamplerModule) {
            await this.load();
        }

        if (this.resampler?.inSampleRate === fromSampleRate && this.resampler.outSampleRate === toSampleRate)
            return this.resampler;

        if (this.resampler)
            this.resampler.delete();
        return this.createResampler(fromSampleRate, toSampleRate);
    }

    private createResampler(fromSampleRate: number, toSampleRate: number): ResamplerWrapper {
        // @ts-expect-error intentional
        const resampler = new ResamplerLoader.resamplerModule.Resampler(fromSampleRate, toSampleRate);
        this.resampler = new ResamplerWrapper(resampler);
        return this.resampler;
    }
}


function getResamplerEmscriptenLoaderOptions(): EmscriptenLoaderOptions {
    return {
        locateFile: (filename: string) => {
            const codecWasmPath = Versioning.mapPath(ResamplerWasm);
            if (filename.endsWith('wasm'))
                return codecWasmPath;

            // Allow secondary resources like the .wasm payload to be loaded by the emscripten code.
            // emscripten 1.37.25 loads memory initializers as data: URI
            else if (filename.startsWith('data:'))
                return filename;
            else throw new Error(`Emscripten module tried to load an unknown file: "${filename}"`);
        },
    };
}
