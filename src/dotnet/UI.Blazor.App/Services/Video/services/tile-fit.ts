// Shared layout helpers for rotated, cover/contain'd video tiles.
// Used by both the receiver render backends and the sender's
// RecorderPreviewView so the sender's self-preview behaves identically.

import { normalizeRotationQuarter, type RotationQuarter } from 'orientation';

export type Fit = 'cover' | 'contain';

// Loss threshold: cover-cropping more than this fraction of the source
// pixel area triggers a switch to contain (+ blurred backdrop). 0.20 =
// "lose ≤ 20% of source to a cover crop". The math (chooseFit below)
// is area-based: `1 − min(frameW·tileH, frameH·tileW) / max(...)`
// equals the fraction of source pixels cropped out by cover-mode fit.
export const COVER_LOSS_MAX = 0.20;

/** Pick cover vs contain for a frame in a tile. `frameW/H` should be
 *  post-rotation visible dims. Falls back to cover when inputs are
 *  unavailable. */
export function chooseFit(frameW: number, frameH: number, tileW: number, tileH: number): Fit {
    if (frameW <= 0 || frameH <= 0 || tileW <= 0 || tileH <= 0) return 'cover';
    const a = frameW * tileH;
    const b = frameH * tileW;
    const cropLoss = 1 - Math.min(a, b) / Math.max(a, b);
    return cropLoss > COVER_LOSS_MAX ? 'contain' : 'cover';
}

/** Publish rotation state to CSS. CSS owns the actual sizing via container
 *  units, so mode changes don't depend on JS reading parent dimensions at
 *  exactly the right time. */
export function applyRotationLayout(el: HTMLElement, quarter: RotationQuarter): void {
    const q = normalizeRotationQuarter(quarter);
    const swap = (q & 1) === 1;
    el.classList.toggle('rotated-video', swap);
    el.style.removeProperty('width');
    el.style.removeProperty('height');
    el.style.removeProperty('left');
    el.style.removeProperty('top');
    el.style.removeProperty('transform');
    el.style.removeProperty('transform-origin');
    if (q === 0)
        el.style.removeProperty('--video-rotation');
    else
        el.style.setProperty('--video-rotation', `${q * 90}deg`);
}
