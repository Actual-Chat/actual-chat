// Shared layout helpers for rotated, cover/contain'd video tiles.
// Used by both the receiver render backends and the sender's
// RecorderPreviewView so the sender's self-preview behaves identically.

import { normalizeRotationQuarter, type RotationQuarter } from 'orientation';

export type Fit = 'cover' | 'contain';

// Loss threshold: when frame and tile orientations DIFFER (one portrait,
// other landscape), cover-cropping more than this fraction of the source
// pixel area triggers a switch to contain (+ blurred backdrop). 0.20 =
// "lose ≤ 20% of source to a cover crop". When orientations MATCH, cover
// is always chosen — symmetric crop within an orientation looks correct
// and avoids paying the 10 Hz WebGL backdrop pump (~10 ms / s).
// The math is area-based: `1 − min(frameW·tileH, frameH·tileW) / max(...)`
// equals the fraction of source pixels cropped out by cover-mode fit.
export const COVER_LOSS_MAX = 0.20;

// Aspect ratios within this distance of 1.0 are treated as "square" and
// fall back to the crop-loss rule. Keeps near-square jitter from flipping
// the cover/contain choice across the orientation boundary.
const SQUARE_DEAD_BAND = 0.05;

/** Pick cover vs contain for a frame in a tile. `frameW/H` should be
 *  post-rotation visible dims. Falls back to cover when inputs are
 *  unavailable.
 *
 *  Same-orientation pairs (both portrait OR both landscape) always pick
 *  cover — symmetric crop within an orientation is acceptable and avoids
 *  the blur backdrop. Cross-orientation pairs use the crop-loss
 *  threshold; cover would crop asymmetrically (e.g. landscape source in
 *  portrait tile loses the sides), so we letterbox + backdrop instead. */
export function chooseFit(frameW: number, frameH: number, tileW: number, tileH: number): Fit {
    if (frameW <= 0 || frameH <= 0 || tileW <= 0 || tileH <= 0) return 'cover';
    const frameAspect = frameW / frameH;
    const tileAspect = tileW / tileH;
    const frameIsLandscape = frameAspect > 1 + SQUARE_DEAD_BAND;
    const frameIsPortrait = frameAspect < 1 - SQUARE_DEAD_BAND;
    const tileIsLandscape = tileAspect > 1 + SQUARE_DEAD_BAND;
    const tileIsPortrait = tileAspect < 1 - SQUARE_DEAD_BAND;
    if ((frameIsLandscape && tileIsLandscape) || (frameIsPortrait && tileIsPortrait))
        return 'cover';
    const a = frameW * tileH;
    const b = frameH * tileW;
    const cropLoss = 1 - Math.min(a, b) / Math.max(a, b);
    return cropLoss > COVER_LOSS_MAX ? 'contain' : 'cover';
}

/** Publish the focused tile's post-rotation aspect to the collapsed (island)
 *  panel via CSS variable + `data-portrait-video` attribute. CSS owns the actual width
 *  / height; this only updates the inputs. No-op when the panel isn't
 *  currently collapsed, when the source dims are missing, or when the
 *  computed ratio is out of the clamped safe range. */
export function updateCollapsedIslandAspect(
    panel: HTMLElement | null,
    frameW: number,
    frameH: number,
): void {
    if (!panel?.classList.contains('collapsed')) return;
    if (frameW <= 0 || frameH <= 0) return;
    const ratio = Math.max(0.25, Math.min(4, frameW / frameH));
    if (!Number.isFinite(ratio)) return;
    const nextAspect = ratio.toFixed(4);
    const nextPortrait = ratio < 1;
    panel.style.setProperty('--video-panel-island-aspect', nextAspect);
    panel.toggleAttribute('data-portrait-video', nextPortrait);
}

/** Publish rotation state to CSS. CSS owns the actual sizing via container
 *  units, so mode changes don't depend on JS reading parent dimensions at
 *  exactly the right time. */
export function applyRotationLayout(el: HTMLElement, quarter: RotationQuarter): void {
    const q = normalizeRotationQuarter(quarter);
    const swap = (q & 1) === 1;
    el.toggleAttribute('data-rotated-video', swap);
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
