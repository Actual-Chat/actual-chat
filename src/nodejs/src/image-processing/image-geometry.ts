export interface ImageSize {
    width: number;
    height: number;
}

export function fitWithin(width: number, height: number, maxSize: number | null): ImageSize {
    const longSide = Math.max(width, height);
    if (maxSize === null || longSide <= maxSize)
        return { width, height };

    const scale = maxSize / longSide;
    return {
        width: Math.max(1, Math.round(width * scale)),
        height: Math.max(1, Math.round(height * scale)),
    };
}
