// Shared settings for the low-resolution background canvas drawn behind the
// letterboxed main video canvas (see `.remote-video-bg` in video-panel.css).

// Background canvas width in pixels; height scales with source aspect.
export const BG_CANVAS_WIDTH = 64;

// Throttle bg redraw — the bitmap is heavily blurred, full fps is wasted GPU work.
export const BG_DRAW_INTERVAL_MS = 100;

// CSS canvas-context filter used by the legacy main-thread canvas backend
// (recorder preview). The off-thread MSTG path uses a WebGPU dual-Kawase
// blur via BgBlurRenderer (see webgpu-blur.ts) and ignores this filter.
export const BG_FILTER = 'blur(3px) saturate(1.2)';

// Kawase blur strength in pixels for the off-thread bg painter. Tuned to
// visually match the previous CPU box blur (radius=2 × 3 passes ≈ Gaussian
// σ≈3) at 64×N output. Drives `cachedLevels` in webgpu-blur.ts: <10 → 2 mip
// levels, which is plenty for a 64-px-wide canvas.
export const BG_BLUR_STRENGTH = 20;
