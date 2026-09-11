import type { ImageFormat, ImageOutputCodec } from './image-processing-contracts';

export type ImageEncoding = 'passthrough' | 'reencode';

export function chooseEncoding(
    format: ImageFormat,
    isAnimated: boolean,
    codec: ImageOutputCodec,
    isOversized: boolean,
): ImageEncoding {
    if (isAnimated || format === 'gif' || format === 'svg' || format === 'unknown')
        return 'passthrough';

    return codec === 'auto' || isOversized ? 'reencode' : 'passthrough';
}
