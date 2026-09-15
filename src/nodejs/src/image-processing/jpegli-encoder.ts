import { getLogs } from 'logging';

const { infoLog } = getLogs('JpegliEncoder');

// The smallest module using a SIMD opcode: WebAssembly.validate rejects it where SIMD is unsupported
const SIMD_PROBE = new Uint8Array([
    0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 96, 0, 1, 123, 3, 2, 1, 0, 10, 10, 1, 8, 0, 65, 0, 253, 15, 253, 98, 11,
]);

export type JpegliVariant = 'simd' | 'scalar';

export interface JpegEncodeOptions {
    distance: number;
    subsampling: 420 | 444;
    progressive: 0 | 1 | 2;
}

interface JpegliModule {
    HEAPU8: Uint8Array;
    HEAPU32: Uint32Array;
    _malloc(size: number): number;
    _free(pointer: number): void;
    _free_buf(pointer: number): void;
    _jpegli_hwy_target(): number;
    _jpegli_encode_rgba(
        rgba: number,
        width: number,
        height: number,
        qualityOrDistance: number,
        useDistance: number,
        subsampling: number,
        progressive: number,
        outSize: number,
    ): number;
    UTF8ToString(pointer: number): string;
}

export class JpegliEncoder {
    private readonly _sizePointer: number;
    private _inputPointer = 0;
    private _inputCapacity = 0;

    private constructor(
        private readonly module: JpegliModule,
        public readonly variant: JpegliVariant,
    ) {
        this._sizePointer = module._malloc(4);
    }

    public static isSimdSupported(): boolean {
        try {
            return WebAssembly.validate(SIMD_PROBE);
        }
        catch {
            return false;
        }
    }

    public static async load(baseUrl: string, variant?: JpegliVariant): Promise<JpegliEncoder> {
        variant ??= JpegliEncoder.isSimdSupported() ? 'simd' : 'scalar';
        const url = `${baseUrl.replace(/\/$/, '')}/${variant}/jpegli.js`;
        const imported = await import(/* webpackIgnore: true */ /* @vite-ignore */ url) as {
            default: () => Promise<JpegliModule>;
        };
        const module = await imported.default();
        infoLog?.log(`load: '${variant}' ready, Highway target ${module.UTF8ToString(module._jpegli_hwy_target())}`);
        return new JpegliEncoder(module, variant);
    }

    public encode(
        rgba: Uint8ClampedArray | Uint8Array,
        width: number,
        height: number,
        options: JpegEncodeOptions,
    ): Uint8Array {
        const length = width * height * 4;
        if (rgba.length < length)
            throw new Error('JpegliEncoder.encode: pixel data is shorter than width * height * 4.');

        const module = this.module;
        if (this._inputCapacity < length) {
            if (this._inputPointer)
                module._free(this._inputPointer);
            this._inputPointer = module._malloc(length);
            if (!this._inputPointer) {
                this._inputCapacity = 0;
                throw new Error('JpegliEncoder.encode: out of memory.');
            }

            this._inputCapacity = length;
        }
        // HEAPU8/HEAPU32 are re-read after every call that may grow memory: growth replaces them
        module.HEAPU8.set(rgba.subarray(0, length), this._inputPointer);
        const outputPointer = module._jpegli_encode_rgba(
            this._inputPointer, width, height, options.distance, 1, options.subsampling, options.progressive,
            this._sizePointer);
        if (!outputPointer)
            throw new Error('JpegliEncoder.encode: jpegli failed.');

        const size = module.HEAPU32[this._sizePointer >> 2];
        const jpeg = module.HEAPU8.slice(outputPointer, outputPointer + size);
        module._free_buf(outputPointer);
        return jpeg;
    }
}
