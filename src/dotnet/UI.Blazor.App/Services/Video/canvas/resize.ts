import type { RotationQuarter } from 'orientation';

export function resizeCanvas(canvas: OffscreenCanvas, width: number, height: number): void {
    if (canvas.width !== width)
        canvas.width = width;
    if (canvas.height !== height)
        canvas.height = height;
}

// CSS object-fit: cover semantics, with an optional quarter-turn applied while
// drawing. The crop is computed in the post-rotation coordinate space so the
// selected sensor rectangle stays stable when Chrome flips source dimensions.
export function drawFrameCover(
    ctx: OffscreenCanvasRenderingContext2D,
    input: VideoFrame,
    targetW: number,
    targetH: number,
    rotation: RotationQuarter = 0,
): void {
    // VideoFrame as CanvasImageSource has intrinsic size = displayWidth/Height
    // (per WebCodecs spec). On Chrome MSTP `crop-and-scale` the coded plane is
    // the camera's native sensor rect while display is the scaled output —
    // sampling with coded coords samples the wrong region. Fall back to coded
    // only if display is unreported (older WebKit before 17.x).
    const inputW = input.displayWidth || input.codedWidth;
    const inputH = input.displayHeight || input.codedHeight;
    const rotatedSrcW = (rotation & 1) === 1 ? inputH : inputW;
    const rotatedSrcH = (rotation & 1) === 1 ? inputW : inputH;
    const cropR = computeCenterCrop(rotatedSrcW, rotatedSrcH, targetW, targetH);

    let sx = 0, sy = 0, sw = 0, sh = 0;
    switch (rotation) {
    case 1: // 90deg CW
        sx = cropR.sy;
        sy = inputH - cropR.sx - cropR.sw;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    case 2: // 180deg
        sx = inputW - cropR.sx - cropR.sw;
        sy = inputH - cropR.sy - cropR.sh;
        sw = cropR.sw;
        sh = cropR.sh;
        break;
    case 3: // 270deg CW
        sx = inputW - cropR.sy - cropR.sh;
        sy = cropR.sx;
        sw = cropR.sh;
        sh = cropR.sw;
        break;
    default:
        sx = cropR.sx; sy = cropR.sy; sw = cropR.sw; sh = cropR.sh;
    }

    ctx.save();
    ctx.translate(targetW / 2, targetH / 2);
    ctx.rotate((rotation * Math.PI) / 2);
    const swap = (rotation & 1) === 1;
    const dw = swap ? targetH : targetW;
    const dh = swap ? targetW : targetH;
    ctx.drawImage(input, sx, sy, sw, sh, -dw / 2, -dh / 2, dw, dh);
    ctx.restore();
}

function computeCenterCrop(
    sx: number, sy: number,
    dx: number, dy: number,
): { sx: number; sy: number; sw: number; sh: number } {
    if (sx <= 0 || sy <= 0 || dx <= 0 || dy <= 0)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    const srcAspect = sx / sy;
    const dstAspect = dx / dy;
    if (Math.abs(srcAspect - dstAspect) < 1e-3)
        return { sx: 0, sy: 0, sw: sx, sh: sy };

    if (srcAspect > dstAspect) {
        const sw = Math.round(sy * dstAspect);
        return { sx: Math.floor((sx - sw) / 2), sy: 0, sw, sh: sy };
    }
    const sh = Math.round(sx / dstAspect);
    return { sx: 0, sy: Math.floor((sy - sh) / 2), sw: sx, sh };
}
