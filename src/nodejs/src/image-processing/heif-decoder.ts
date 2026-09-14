import { getLogs } from 'logging';
import { readHeifOrientation } from './image-format';

const { infoLog } = getLogs('HeifDecoder');

export interface RgbaImage {
    // Not the plain Uint8ClampedArray: ImageData rejects one backed by a SharedArrayBuffer
    data: Uint8ClampedArray<ArrayBuffer>;
    width: number;
    height: number;
}

interface LibheifImage {
    get_width(): number;
    get_height(): number;
    is_primary(): boolean;
    free(): void;
    display(target: RgbaImage, callback: (result: RgbaImage | null) => void): void;
}

interface LibheifModule {
    HeifDecoder: new () => { decode(bytes: Uint8Array): LibheifImage[] };
}

type LibheifFactory = (options: { wasmBinary: ArrayBuffer }) => LibheifModule;

/** HEIC/HEIF decoder for browsers that have none - Chromium cannot decode HEIC at all, and
 *  Firefox lacks HEVC on most platforms. WebKit decodes it natively and never loads this. */
export class HeifDecoder {
    private constructor(private readonly module: LibheifModule) {}

    public static async load(baseUrl: string): Promise<HeifDecoder> {
        const base = baseUrl.replace(/\/$/, '');
        // The glue instantiates the wasm synchronously, and the loaders it would use for that
        // (XHR, importScripts) don't exist in a module worker - so the binary is handed to it here
        const response = await fetch(`${base}/libheif.wasm`);
        if (!response.ok)
            throw new Error(`HeifDecoder.load: HTTP ${response.status} while fetching libheif.wasm.`);

        const wasmBinary = await response.arrayBuffer();
        const imported = await import(/* webpackIgnore: true */ /* @vite-ignore */ `${base}/libheif.js`) as {
            default: LibheifFactory;
        };
        infoLog?.log(`load: ready, ${wasmBinary.byteLength} bytes of wasm`);
        return new HeifDecoder(imported.default({ wasmBinary }));
    }

    /** Applies EXIF Orientation, which libheif ignores and WebKit applies: without it the same
     *  photo arrives upright from Safari and sideways from Chrome. */
    public async decode(bytes: Uint8Array): Promise<RgbaImage | null> {
        const images = new this.module.HeifDecoder().decode(bytes);
        if (images.length === 0)
            return null;

        try {
            const image = images.find(x => x.is_primary()) ?? images[0];
            const width = image.get_width();
            const height = image.get_height();
            const target: RgbaImage = { data: new Uint8ClampedArray(width * height * 4), width, height };
            const decoded = await new Promise<RgbaImage | null>(resolve => image.display(target, resolve));
            return decoded && applyOrientation(decoded, readHeifOrientation(bytes));
        }
        finally {
            for (const image of images)
                image.free();
        }
    }
}

// Private methods

function applyOrientation(image: RgbaImage, orientation: number): RgbaImage {
    if (orientation <= 1 || orientation > 8)
        return image;

    const { width, height } = image;
    const isTransposed = orientation >= 5;
    const result: RgbaImage = {
        data: new Uint8ClampedArray(width * height * 4),
        width: isTransposed ? height : width,
        height: isTransposed ? width : height,
    };
    // Whole pixels are moved, so the byte order within one never matters
    const source = new Uint32Array(image.data.buffer, image.data.byteOffset, width * height);
    const target = new Uint32Array(result.data.buffer);
    const [base, stepX, stepY] = getOrientationSteps(orientation, width, height);
    let sourceIndex = 0;
    for (let y = 0; y < height; y++) {
        let targetIndex = base + y * stepY;
        for (let x = 0; x < width; x++) {
            target[targetIndex] = source[sourceIndex++];
            targetIndex += stepX;
        }
    }

    return result;
}

/** Where source pixel (0, 0) lands and how one step along each source axis moves in the target:
 *  target index = base + x * stepX + y * stepY. */
function getOrientationSteps(orientation: number, width: number, height: number): [number, number, number] {
    switch (orientation) {
    case 2: // Mirrored
        return [width - 1, -1, width];
    case 3: // Rotated 180
        return [width * height - 1, -1, -width];
    case 4: // Mirrored, rotated 180
        return [(height - 1) * width, 1, -width];
    case 5: // Mirrored, rotated 90 CCW
        return [0, height, 1];
    case 6: // Rotated 90 CW
        return [height - 1, height, -1];
    case 7: // Mirrored, rotated 90 CW
        return [width * height - 1, -height, -1];
    case 8: // Rotated 90 CCW
        return [(width - 1) * height, -height, 1];
    default:
        return [0, 1, width];
    }
}
