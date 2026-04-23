import { getCodecCategory } from './codec-support';

type Tier = 1080 | 720 | 540 | 360;
type Category = 'h264' | 'hevc' | 'vp9' | 'av1';
export type StreamMode = 'webcam' | 'screen';

const BITRATE_TABLE: Record<Category, Record<Tier, number>> = {
    h264: { 1080: 6_500_000, 720: 4_000_000, 540: 2_500_000, 360: 1_250_000 },
    hevc: { 1080: 3_250_000, 720: 2_000_000, 540: 1_250_000, 360:   650_000 },
    vp9:  { 1080: 2_750_000, 720: 1_600_000, 540: 1_050_000, 360:   550_000 },
    av1:  { 1080: 2_250_000, 720: 1_400_000, 540: 1_000_000, 360:   500_000 },
};

// Screen content (IDE text, UI chrome) has much higher spatial entropy than
// camera video. ~1.75x the camera budget keeps 10-12pt text readable at the
// same resolution on a cross-continent link. Keep in sync with VideoBitrateTable.cs.
const SCREEN_MULTIPLIER = 1.75;

export function getExpectedBitrate(codec: string, height: number, mode: StreamMode = 'webcam'): number {
    const category = getCodecCategory(codec);
    const base = BITRATE_TABLE[category][pickTier(height)];
    return mode === 'screen' ? Math.round(base * SCREEN_MULTIPLIER) : base;
}

function pickTier(height: number): Tier {
    if (height >= 1080) return 1080;
    if (height >= 720) return 720;
    if (height >= 540) return 540;
    return 360;
}
