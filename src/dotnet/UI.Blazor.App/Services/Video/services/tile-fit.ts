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

/** CSS transform is post-layout, so for odd quarters we also transpose
 *  the element's layout box: it's sized to the parent's swapped dims,
 *  centered absolutely, then rotated. That way `object-fit: cover|contain`
 *  operates on the element's natural pre-rotation aspect. Even quarters
 *  keep the default `width:100%; height:100%` layout. */
export function applyRotationLayout(el: HTMLElement, quarter: RotationQuarter): void {
    const q = normalizeRotationQuarter(quarter);
    const swap = (q & 1) === 1;
    if (!swap) {
        el.style.removeProperty('width');
        el.style.removeProperty('height');
        el.style.removeProperty('left');
        el.style.removeProperty('top');
        if (q === 0) {
            el.style.removeProperty('transform');
            return;
        }
        el.style.transform = `rotate(${q * 90}deg)`;
        el.style.transformOrigin = 'center center';
        return;
    }
    const parent = el.parentElement;
    if (!parent) return;
    const rect = parent.getBoundingClientRect();
    const w = rect.width > 0 ? rect.width : parent.clientWidth;
    const h = rect.height > 0 ? rect.height : parent.clientHeight;
    if (w <= 0 || h <= 0) return;
    el.style.width = `${h}px`;
    el.style.height = `${w}px`;
    el.style.left = '50%';
    el.style.top = '50%';
    el.style.transformOrigin = 'center center';
    el.style.transform = `translate(-50%, -50%) rotate(${q * 90}deg)`;
}
