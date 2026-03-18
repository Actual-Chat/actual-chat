/**
 * Codec Support Detection
 * Detects which video codecs are supported by the browser
 */

import { Log } from 'logging';
import { DeviceInfo } from 'device-info';

const { errorLog } = Log.get('VideoDecoder');

export interface CodecInfo {
    name: string;
    codec: string;
    category: 'h264' | 'hevc' | 'av1';
    supported: boolean;
    hardwareAccelerated: boolean;
    scalabilityModes: string[];
}

const CODEC_PROFILES = {
    h264: [
        { name: 'H.264 Baseline 3.0', codec: 'avc1.42E01E' },
        { name: 'H.264 Baseline 3.1', codec: 'avc1.42E01F' },
        { name: 'H.264 Main 3.0', codec: 'avc1.4D401E' },
        { name: 'H.264 Main 3.1', codec: 'avc1.4D401F' },
        { name: 'H.264 High 3.0', codec: 'avc1.64001E' },
        { name: 'H.264 High 3.1', codec: 'avc1.64001F' },
        { name: 'H.264 High 4.0', codec: 'avc1.640028' },
        { name: 'H.264 High 4.1', codec: 'avc1.640029' },
    ],
    hevc: [
        { name: 'HEVC Main, Level 3.0', codec: 'hev1.1.6.L90.B0' },
        { name: 'HEVC Main, Level 3.1', codec: 'hev1.1.6.L93.B0' },
        { name: 'HEVC Main, Level 4.0', codec: 'hev1.1.6.L120.B0' },
        { name: 'HEVC Main, Level 4.1', codec: 'hev1.1.6.L123.B0' },
        { name: 'HEVC Main 10, Level 4.0', codec: 'hev1.2.4.L120.B0' },
        { name: 'HEVC Main 10, Level 4.1', codec: 'hev1.2.4.L123.B0' },
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

    return results;
}

async function isCodecSupported(
    codec: string,
    category: 'h264' | 'hevc' | 'av1',
    width: number,
    height: number
): Promise<{ supported: boolean; hardwareAccelerated: boolean; scalabilityModes: string[] }> {
    try {
        const config: VideoEncoderConfig = {
            codec,
            width,
            height,
            bitrate: 5_000_000,
            framerate: 30,
            latencyMode: 'realtime',
            hardwareAcceleration: 'prefer-hardware',
        };

        // Add codec-specific config
        if (category === 'h264') {
            config.avc = { format: 'avc' };
        }
        // HEVC and AV1 don't need additional config properties

        const support = await VideoEncoder.isConfigSupported(config);

        // Check if hardware acceleration is actually being used
        // The config in the support response will have 'no-preference' if hardware isn't available
        const hardwareAccelerated =
            support.supported &&
            (support.config?.hardwareAcceleration === 'prefer-hardware' || false);

        // Detect supported scalability modes
        const scalabilityModes: string[] = [];
        if (support.supported) {
            // Common scalability modes to test
            const modesToTest = ['L1T1', 'L1T2', 'L1T3'];

            for (const mode of modesToTest) {
                try {
                    const testConfig: VideoEncoderConfig = {
                        ...config,
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

        return {
            supported: support.supported ?? false,
            hardwareAccelerated: hardwareAccelerated ?? false,
            scalabilityModes,
        };
    } catch (error) {
        errorLog?.log(`Error checking codec support for ${codec}:`, error);
        return { supported: false, hardwareAccelerated: false, scalabilityModes: [] };
    }
}

export function getCodecCategory(codecString: string): 'h264' | 'hevc' | 'av1' {
    if (codecString.startsWith('av01')) return 'av1';
    if (codecString.startsWith('hev1') || codecString.startsWith('hvc1')) return 'hevc';
    return 'h264';
}

export function getCodecForCategory(category: 'h264' | 'hevc' | 'av1', width: number, height: number): string {
    const isMobile = DeviceInfo.isMobile; // includes iOS
    const isHighRes = height > 720;

    if (category === 'av1') {
        return isHighRes ? 'av01.0.08M.08' : 'av01.0.05M.08'; // Main L4.0 / Main L3.0
    }
    if (category === 'hevc') {
        return isHighRes ? 'hev1.1.6.L120.B0' : 'hev1.1.6.L93.B0'; // Main L4.0 / Main L3.1
    }
    // H.264: Mobile uses Main profile (power efficient), Desktop uses High (better compression)
    if (isMobile) {
        return 'avc1.4D401F'; // Main 3.1 — mobile is capped at ≤720p
    }
    return isHighRes ? 'avc1.640028' : 'avc1.64001F'; // High 4.0 / High 3.1
}

export function getDefaultCodec(supportedCodecs: CodecInfo[], width = 1280, height = 720): string {
    const isMobile = DeviceInfo.isMobile; // includes iOS

    // Priority: AV1 HW > HEVC HW > H.264 HW (profile by platform) > H.264 SW (profile by platform)

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

    // H.264 profile preference depends on platform
    // Mobile: Main > Baseline > High (power efficient)
    // Desktop: High > Main > any (better compression)
    const h264ProfileOrder = isMobile
        ? ['4D40', '42E0', '6400']  // Main, Baseline, High
        : ['6400', '4D40'];         // High, Main

    // 3. Try H.264 HW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.hardwareAccelerated && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 3b. Any H.264 HW
    const anyH264HW = supportedCodecs.find(
        c => c.category === 'h264' && c.supported && c.hardwareAccelerated
    );
    if (anyH264HW) return anyH264HW.codec;

    // 4. Try H.264 SW in profile preference order
    for (const profile of h264ProfileOrder) {
        const match = supportedCodecs.find(
            c => c.category === 'h264' && c.supported && c.codec.includes(profile)
        );
        if (match) return match.codec;
    }
    // 4b. Any H.264 SW
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

export async function detectSupportedDecoderCodecs(): Promise<string[]> {
    const codecs: string[] = ['h264']; // H.264 always assumed supported
    try {
        const av1 = await VideoDecoder.isConfigSupported({
            codec: 'av01.0.08M.08',
            codedWidth: 1280,
            codedHeight: 720,
        });
        if (av1.supported) codecs.push('av1');
    } catch { /* not supported */ }
    // Try hev1 (Chrome/desktop) then hvc1 (iOS Safari) for HEVC decoder detection
    let hevcSupported = false;
    for (const hevcCodec of ['hev1.1.6.L120.B0', 'hvc1.1.6.L93.90']) {
        try {
            const hevc = await VideoDecoder.isConfigSupported({
                codec: hevcCodec,
                codedWidth: 1280,
                codedHeight: 720,
            });
            if (hevc.supported) { hevcSupported = true; break; }
        } catch { /* not supported */ }
    }
    if (hevcSupported) codecs.push('hevc');
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
