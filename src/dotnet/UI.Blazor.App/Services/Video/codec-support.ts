/**
 * Codec Support Detection
 * Detects which video codecs are supported by the browser
 */

import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

const { debugLog, warnLog, errorLog } = Log.get('VideoPipeline');

export interface CodecInfo {
    name: string;
    codec: string;
    category: 'h264' | 'hevc' | 'av1' | 'vp9';
    supported: boolean;
    hardwareAccelerated: boolean;
    scalabilityModes: string[];
}

const CODEC_PROFILES = {
    h264: [
        { name: 'H.264 High 4.1', codec: 'avc1.640029' },
        { name: 'H.264 High 4.0', codec: 'avc1.640028' },
        { name: 'H.264 High 3.1', codec: 'avc1.64001F' },
        { name: 'H.264 High 3.0', codec: 'avc1.64001E' },
        { name: 'H.264 Main 3.1', codec: 'avc1.4D401F' },
        { name: 'H.264 Main 3.0', codec: 'avc1.4D401E' },
        { name: 'H.264 Baseline 3.1', codec: 'avc1.42E01F' },
        { name: 'H.264 Baseline 3.0', codec: 'avc1.42E01E' },
    ],
    hevc: [
        { name: 'HEVC Main, Level 4.1', codec: 'hev1.1.6.L123.B0' },
        { name: 'HEVC Main, Level 4.0', codec: 'hev1.1.6.L120.B0' },
        { name: 'HEVC Main, Level 3.1', codec: 'hev1.1.6.L93.B0' },
        { name: 'HEVC Main, Level 3.0', codec: 'hev1.1.6.L90.B0' },
        { name: 'HEVC Main 10, Level 4.0', codec: 'hev1.2.4.L120.B0' },
        { name: 'HEVC Main 10, Level 4.1', codec: 'hev1.2.4.L123.B0' },
    ],
    vp9: [
        { name: 'VP9 Profile 0, Level 4.1', codec: 'vp09.00.41.08' },
        { name: 'VP9 Profile 0, Level 4.0', codec: 'vp09.00.40.08' },
        { name: 'VP9 Profile 0, Level 3.1', codec: 'vp09.00.31.08' },
        { name: 'VP9 Profile 0, Level 3.0', codec: 'vp09.00.30.08' },
    ],
    av1: [
        { name: 'AV1 Main, Level 3.0', codec: 'av01.0.05M.08' },
        { name: 'AV1 Main, Level 4.0', codec: 'av01.0.08M.08' },
        { name: 'AV1 Main, Level 4.1', codec: 'av01.0.09M.08' },
        { name: 'AV1 Main, Level 5.0', codec: 'av01.0.12M.08' },
        { name: 'AV1 High, Level 4.0', codec: 'av01.1.08M.08' },
        { name: 'AV1 High, Level 4.1', codec: 'av01.1.09M.08' },
    ],
};

export async function detectSupportedCodecs(width = 1920, height = 1080): Promise<CodecInfo[]> {
    const results: CodecInfo[] = [];

    // Check H.264 codecs
    for (const profile of CODEC_PROFILES.h264) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'h264', width, height);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'h264',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    // Check HEVC codecs
    for (const profile of CODEC_PROFILES.hevc) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'hevc', width, height);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'hevc',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    // Check VP9 codecs
    for (const profile of CODEC_PROFILES.vp9) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'vp9', width, height);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'vp9',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    // Check AV1 codecs
    for (const profile of CODEC_PROFILES.av1) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'av1', width, height);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'av1',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    // Diagnostic summary
    const supported = results.filter(c => c.supported);
    warnLog?.log(`ENCODER_CODECS: ${supported.map(c =>
        `${c.codec}(${c.hardwareAccelerated ? 'hw' : 'sw'})`).join(', ') || 'none'}`);

    return results;
}

async function isCodecSupported(
    codec: string,
    category: 'h264' | 'hevc' | 'av1' | 'vp9',
    width: number,
    height: number
): Promise<{ supported: boolean; hardwareAccelerated: boolean; scalabilityModes: string[] }> {
    try {
        const baseConfig: VideoEncoderConfig = {
            codec,
            width,
            height,
            bitrate: 5_000_000,
            framerate: 30,
            latencyMode: 'realtime',
        };

        // Add codec-specific config
        if (category === 'h264') {
            baseConfig.avc = { format: 'avc' };
        }

        // Try hardware-accelerated first, then software fallback
        // Firefox often returns supported: false for 'prefer-hardware' but works with 'no-preference'
        let supported = false;
        let hardwareAccelerated = false;

        for (const accel of ['prefer-hardware', 'no-preference'] as const) {
            const config = { ...baseConfig, hardwareAcceleration: accel };
            const support = await VideoEncoder.isConfigSupported(config);
            if (support.supported) {
                supported = true;
                hardwareAccelerated = accel === 'prefer-hardware'
                    && (support.config?.hardwareAcceleration === 'prefer-hardware');
                break;
            }
        }

        // Detect supported scalability modes
        const scalabilityModes: string[] = [];
        if (supported) {
            const modesToTest = ['L1T1', 'L1T2', 'L1T3'];
            for (const mode of modesToTest) {
                try {
                    const testConfig: VideoEncoderConfig = {
                        ...baseConfig,
                        hardwareAcceleration: hardwareAccelerated ? 'prefer-hardware' : 'no-preference',
                        scalabilityMode: mode,
                    };
                    const modeSupport = await VideoEncoder.isConfigSupported(testConfig);
                    if (modeSupport.supported) {
                        scalabilityModes.push(mode);
                    }
                } catch {
                    // Mode not supported, continue
                }
            }
        }

        debugLog?.log(`Encoder ${codec}: ${supported ? `supported, hw=${hardwareAccelerated}` : 'not supported'}`);
        return { supported, hardwareAccelerated, scalabilityModes };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { supported: false, hardwareAccelerated: false, scalabilityModes: [] };
    }
}

export function getCodecCategory(codecString: string): 'h264' | 'hevc' | 'av1' | 'vp9' {
    if (codecString.startsWith('av01')) return 'av1';
    if (codecString.startsWith('hev1') || codecString.startsWith('hvc1')) return 'hevc';
    if (codecString.startsWith('vp09')) return 'vp9';
    return 'h264';
}

export function getCodecForCategory(category: 'h264' | 'hevc' | 'av1' | 'vp9', width: number, height: number): string {
    const isMobile = DeviceInfo.isMobile; // includes iOS
    const isHighRes = height > 720;

    if (category === 'av1') {
        return isHighRes ? 'av01.0.08M.08' : 'av01.0.05M.08'; // Main L4.0 / Main L3.0
    }
    if (category === 'vp9') {
        return isHighRes ? 'vp09.00.41.08' : 'vp09.00.31.08'; // Profile 0 L4.1 / L3.1
    }
    if (category === 'hevc') {
        return isHighRes ? 'hev1.1.6.L120.B0' : 'hev1.1.6.L93.B0'; // Main L4.0 / Main L3.1
    }
    // H.264: Firefox and mobile use Main profile, desktop uses High (better compression)
    if (isMobile || DeviceInfo.isFirefox) {
        return 'avc1.4D401F'; // Main 3.1
    }
    return isHighRes ? 'avc1.640028' : 'avc1.64001F'; // High 4.0 / High 3.1
}

export function getDefaultCodec(supportedCodecs: CodecInfo[], width = 1280, height = 720): string {
    // Firefox: force H.264 Main 3.1 — only reliable encoder profile
    if (DeviceInfo.isFirefox) {
        return 'avc1.4D401F';
    }

    const isMobile = DeviceInfo.isMobile; // includes iOS

    // Priority: AV1 HW > HEVC HW > VP9 HW > H.264 HW (profile by platform) > H.264 SW

    // 1. Try AV1 with hardware acceleration
    const av1HW = supportedCodecs.find(
        c => c.category === 'av1' && c.supported && c.hardwareAccelerated
    );
    if (av1HW) return av1HW.codec;

    // 2. Try HEVC with hardware acceleration
    const hevcHW = supportedCodecs.find(
        c => c.category === 'hevc' && c.supported && c.hardwareAccelerated
    );
    if (hevcHW) return hevcHW.codec;

    // 3. Try VP9 with hardware acceleration
    const vp9HW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported && c.hardwareAccelerated
    );
    if (vp9HW) return vp9HW.codec;

    // H.264 profile preference depends on platform
    // Mobile: Main > Baseline > High (power efficient)
    // Desktop: High > Main > any (better compression)
    const h264ProfileOrder = isMobile
        ? ['4D40', '42E0', '6400']  // Main, Baseline, High
        : ['6400', '4D40'];         // High, Main

    // 4. Try H.264 HW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.hardwareAccelerated && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 4b. Any H.264 HW
    const anyH264HW = supportedCodecs.find(
        c => c.category === 'h264' && c.supported && c.hardwareAccelerated
    );
    if (anyH264HW) return anyH264HW.codec;

    // 5. Try VP9 SW (better compression than H.264 SW)
    const vp9SW = supportedCodecs.find(
        c => c.category === 'vp9' && c.supported
    );
    if (vp9SW) return vp9SW.codec;

    // 6. Try H.264 SW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 5b. Any H.264 SW
    const anyH264 = supportedCodecs.find(
        c => c.category === 'h264' && c.supported
    );
    if (anyH264) return anyH264.codec;

    // Fallback: resolution-aware codec string
    return getCodecForCategory('h264', width, height);
}

export async function getAV1CodecSupport(): Promise<CodecInfo[]> {
    const results: CodecInfo[] = [];

    // Check AV1 codecs
    for (const profile of CODEC_PROFILES.av1) {
        const { supported, hardwareAccelerated, scalabilityModes } = await isCodecSupported(profile.codec, 'av1', 1920, 1080);
        results.push({
            name: profile.name,
            codec: profile.codec,
            category: 'av1',
            supported,
            hardwareAccelerated,
            scalabilityModes,
        });
    }

    return results;
}

/** Check decoder support with prefer-hardware → no-preference fallback (matches video-player.ts init) */
async function isDecoderCodecSupported(codec: string, width: number, height: number): Promise<boolean> {
    for (const accel of ['prefer-hardware', 'no-preference'] as const) {
        try {
            const { supported } = await VideoDecoder.isConfigSupported({
                codec,
                codedWidth: width,
                codedHeight: height,
                hardwareAcceleration: accel,
            });
            if (supported) return true;
        } catch { /* continue */ }
    }
    return false;
}

// Runtime codec exclusion — codecs that technically decode but can't sustain realtime throughput
const excludedDecoderCodecs = new Set<string>();

/** Exclude a decoder codec category at runtime (persists for JS module lifetime). */
export function excludeDecoderCodec(codec: string): void {
    if (codec === 'h264') return; // never exclude h264 — universal fallback
    warnLog?.log(`Excluding decoder codec: ${codec}`);
    excludedDecoderCodecs.add(codec);
}

/** Get the set of runtime-excluded decoder codecs. */
export function getExcludedDecoderCodecs(): string[] {
    return [...excludedDecoderCodecs];
}

export async function detectSupportedDecoderCodecs(): Promise<string[]> {
    const codecs: string[] = ['h264']; // H.264 always assumed supported

    // AV1 — test both levels actually used in practice
    if (!excludedDecoderCodecs.has('av1')) {
        try {
            const av1Supported = await isDecoderCodecSupported('av01.0.08M.08', 1280, 720)
                || await isDecoderCodecSupported('av01.0.05M.08', 1280, 720);
            warnLog?.log(`Decoder AV1 (av01.0.08M.08): supported=${av1Supported}`);
            if (av1Supported) codecs.push('av1');
        } catch (e) {
            warnLog?.log(`Decoder AV1 (av01.0.08M.08): error=${e}`);
        }
    } else {
        warnLog?.log(`Decoder AV1: excluded at runtime (too slow for realtime)`);
    }

    // HEVC — try multiple codec strings
    if (!excludedDecoderCodecs.has('hevc')) {
        let hevcSupported = false;
        for (const hevcCodec of [
            'hev1.1.6.L120.B0',    // hev1 Main L4.0
            'hev1.1.6.L93.B0',     // hev1 Main L3.1
            'hvc1.1.6.L120.B0',    // hvc1 Main L4.0 (iOS Safari)
            'hvc1.1.6.L93.90',     // hvc1 Main L3.1 (iOS Safari variant)
        ]) {
            try {
                if (await isDecoderCodecSupported(hevcCodec, 1280, 720)) {
                    warnLog?.log(`Decoder HEVC (${hevcCodec}): supported=true`);
                    hevcSupported = true;
                    break;
                }
                warnLog?.log(`Decoder HEVC (${hevcCodec}): supported=false`);
            } catch (e) {
                warnLog?.log(`Decoder HEVC (${hevcCodec}): error=${e}`);
            }
        }
        if (hevcSupported) codecs.push('hevc');
    } else {
        warnLog?.log(`Decoder HEVC: excluded at runtime (too slow for realtime)`);
    }

    // VP9
    if (!excludedDecoderCodecs.has('vp9')) {
        try {
            const vp9Supported = await isDecoderCodecSupported('vp09.00.31.08', 1280, 720);
            warnLog?.log(`Decoder VP9 (vp09.00.31.08): supported=${vp9Supported}`);
            if (vp9Supported) codecs.push('vp9');
        } catch (e) {
            warnLog?.log(`Decoder VP9 (vp09.00.31.08): error=${e}`);
        }
    } else {
        warnLog?.log(`Decoder VP9: excluded at runtime (too slow for realtime)`);
    }

    warnLog?.log(`DECODER_CODECS: [${codecs.join(', ')}]${excludedDecoderCodecs.size > 0 ? ` (excluded: [${[...excludedDecoderCodecs].join(', ')}])` : ''}`);
    return codecs;
}

export function getBestScalabilityMode(scalabilityModes: string[]): string | undefined {
    // Priority: L1T1 > L1T2 > L1T3
    if (scalabilityModes.includes('L1T1')) {
        return 'L1T1';
    }
    if (scalabilityModes.includes('L1T2')) {
        return 'L1T2';
    }
    if (scalabilityModes.includes('L1T3')) {
        return 'L1T3';
    }
    return undefined;
}
