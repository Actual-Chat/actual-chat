// Maps a canvas client width (in CSS pixels) to a VideoQualityLevel index that
// the server uses to clamp this peer's spatial cap. Lower index = higher quality.
// Returns null when the canvas hasn't laid out yet — server applies no cap and
// the join lands at the top spatial layer.
//   level 4 = Low      (small sidebar tile)
//   level 3 = Medium
//   level 2 = High
//   level 1 = Full
//   level 0 = Ultra    (4K-class layouts)
export function renderQualityLevelForWidth(w: number): number | null {
    if (w <= 0) return null;
    if (w <= 480) return 4;
    if (w <= 720) return 3;
    if (w <= 1280) return 2;
    if (w <= 1920) return 1;
    return 0;
}
