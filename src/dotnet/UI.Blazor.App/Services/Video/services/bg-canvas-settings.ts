// Shared settings for the low-resolution background canvas drawn behind the
// letterboxed main video canvas (see `.remote-video-bg` in video-panel.css).

// Background canvas width in pixels; height scales with source aspect.
export const BG_CANVAS_WIDTH = 64;

// Throttle bg redraw — the bitmap is heavily blurred, full fps is wasted GPU work.
export const BG_DRAW_INTERVAL_MS = 100;

// CSS canvas-context filter used by the legacy main-thread canvas backend
// (recorder preview). The off-thread MSTG path (worker-mstg-selector) does
// not use ctx.filter — Safari OffscreenCanvas silently ignores it on some
// versions, leaving the bg pixelated. The off-thread path runs a portable
// software box-blur instead (see `bgBoxBlur` in worker-mstg-selector.ts).
export const BG_FILTER = 'blur(3px) saturate(1.2)';

// Box-blur kernel size used by the off-thread bg painter. radius=2 with 3
// passes ≈ Gaussian blur σ≈3, applied to a 64×N image at 10 fps — the JS
// cost is microseconds and the result is browser-portable.
export const BG_BOX_BLUR_RADIUS = 2;
export const BG_BOX_BLUR_PASSES = 3;
