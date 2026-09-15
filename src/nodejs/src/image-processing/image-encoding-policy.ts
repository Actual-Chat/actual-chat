import type { ImageFormat, ImageOutput, ImageOutputCodec, ImageOutputSpec } from './image-processing-contracts';
import type { ImageSize } from './image-geometry';
import { getImageMimeType } from './image-format';
import { stripImageMetadata } from './metadata-stripper';

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

/** The stripped source of an unscaled image, when re-encoding it to encodedSize didn't pay off; null otherwise. */
export function tryKeepSource(
    source: Blob,
    bytes: Uint8Array,
    format: ImageFormat,
    spec: ImageOutputSpec,
    size: ImageSize,
    encodedSize: number,
): ImageOutput | null {
    const stripped = stripImageMetadata(bytes, format);
    if (stripped.length > encodedSize)
        return null;

    const mimeType = getImageMimeType(format);
    const isSource = stripped === bytes;
    const blob = isSource ? source : new Blob([stripped as BlobPart], { type: mimeType });
    return { kind: spec.kind, blob, mimeType, width: size.width, height: size.height, isSource };
}

/** False only for formats that can never carry alpha, so the per-pixel scan can be skipped. */
export function canHaveAlpha(format: ImageFormat): boolean {
    return format !== 'jpeg';
}
