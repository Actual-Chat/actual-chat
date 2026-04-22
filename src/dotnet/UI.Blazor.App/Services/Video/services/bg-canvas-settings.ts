// Shared settings for the low-resolution background canvas drawn behind the
// letterboxed main video canvas (see `.remote-video-bg` in video-panel.css).

// Background canvas width in pixels; height scales with source aspect.
export const BG_CANVAS_WIDTH = 64;

// Throttle bg redraw — the bitmap is heavily blurred, full fps is wasted GPU work.
export const BG_DRAW_INTERVAL_MS = 100;

// Blur + saturate baked into the bg bitmap so the CSS layer doesn't need `filter:` —
// a CSS blur of the upscaled-and-composited canvas was the dominant paint cost.
export const BG_FILTER = 'blur(3px) saturate(1.3)';
