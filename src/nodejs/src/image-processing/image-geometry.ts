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

export function fitWithinBudget(
    width: number,
    height: number,
    maxPixels: number | null,
    maxLongSide: number | null,
): ImageSize {
    const pixelScale = maxPixels === null ? 1 : Math.sqrt(maxPixels / (width * height));
    const longSide = Math.max(width, height);
    const sideScale = maxLongSide === null ? 1 : maxLongSide / longSide;
    const scale = Math.min(1, pixelScale, sideScale);
    if (scale >= 1)
        return { width, height };

    // sqrt/division above can leave a dimension a hair under its true integer value
    // (e.g. 20000 * (12288 / 20000) === 12287.999999999998), so floor would clip it
    // one pixel short; the epsilon absorbs that noise without risking the budget.
    const epsilon = 1e-9;
    return {
        width: Math.max(1, Math.floor(width * scale + epsilon)),
        height: Math.max(1, Math.floor(height * scale + epsilon)),
    };
}
