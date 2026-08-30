// HEVC codec-string derivation + WebCodecs decoder candidate selection.
// Candidates are probed hardware-first, then again with no-preference before
// giving up — Firefox routinely rejects 'prefer-hardware' for a codec it can
// decode in software, and the encoder-side probe has always allowed for that.

import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoDecoder');

export interface DecoderDimensions {
    width: number;
    height: number;
}

export type DecoderHardwareAcceleration = 'prefer-hardware' | 'no-preference';

export interface DecoderCodecSelection {
    codec: string;
    hardwareAcceleration: DecoderHardwareAcceleration;
}

// HEVC: tries high-level candidates using SPS tier (bitstream truth), then
// HVCC-header, opposite-tier, and declared fallbacks. Other codecs: single
// candidate from the legacy mapping.
export function getCodecCandidates(codec: string, description?: ArrayBuffer): string[] {
    if (description && description.byteLength >= 5 && isHvccDescription(description)) {
        const bytes = new Uint8Array(description);
        const generalProfileIdc = bytes[1] & 0x1F;
        const tier = (bytes[1] >> 5) & 0x01;
        const tierStr = tier ? 'H' : 'L';

        const candidates: string[] = [];
        const seen = new Set<string>();
        const addCandidate = (c: string) => {
            if (seen.has(c))
                return;

            seen.add(c);
            candidates.push(c);
        };

        // Ordering: each simulcast layer ships its own HVCC with per-layer
        // level_idc (e.g. 360p base → L63=2.1). Chrome's isConfigSupported
        // does NOT cross-check codec-string level vs description level —
        // configure() succeeds but decode() silently drops chunks whose
        // bitstream level exceeds the declared one. Always probe a high-enough
        // level first.
        // Tier must come from SPS when available: Chrome's HEVC encoder can
        // write HVCC tier=Low while SPS says High; iOS HEVC HW rejects the
        // mismatch and Edge/Chrome HW can silently stall.
        const spsTier = extractHvccSpsTierFlag(bytes);
        const spsTierStr: 'H' | 'L' | undefined = spsTier === 1 ? 'H' : (spsTier === 0 ? 'L' : undefined);
        const preferredTier = spsTierStr ?? tierStr;

        // Sender's declared string carries the ladder-top level; reconcile tier with SPS.
        const lc = codec.toLowerCase();
        if (lc.startsWith('hev1.') || lc.startsWith('hvc1.'))
            addCandidate(replaceHevcTier(codec, preferredTier));

        // Hardcoded L4.0 fallback (admits up to 1080p).
        addCandidate(`hev1.${generalProfileIdc}.6.${preferredTier}120.B0`);
        addCandidate(`hvc1.${generalProfileIdc}.6.${preferredTier}120.B0`);

        // Derived from HVCC binary (ground truth for THIS layer).
        if (spsTierStr) {
            addCandidate(deriveHevcCodecString('hev1', bytes, spsTierStr));
            addCandidate(deriveHevcCodecString('hvc1', bytes, spsTierStr));
        }
        addCandidate(deriveHevcCodecString('hev1', bytes));
        addCandidate(deriveHevcCodecString('hvc1', bytes));
        // Belt-and-suspenders: opposite tier in case neither HVCC nor SPS landed correctly.
        addCandidate(deriveHevcCodecString('hev1', bytes, 'H'));
        addCandidate(deriveHevcCodecString('hvc1', bytes, 'H'));
        addCandidate(deriveHevcCodecString('hev1', bytes, 'L'));
        addCandidate(deriveHevcCodecString('hvc1', bytes, 'L'));
        if (lc.startsWith('hev1.') || lc.startsWith('hvc1.'))
            addCandidate(codec);

        const declaredLower = codec.toLowerCase();
        if (!declaredLower.startsWith('hev1') && !declaredLower.startsWith('hvc1')
            && declaredLower !== 'hevc' && declaredLower !== 'h265')
            warnLog?.log(`Codec mismatch: declared=${codec} but description is HVCC`);

        return candidates;
    }

    const mapped = mapCodecToWebCodecs(codec, description);
    if (!mapped.startsWith('avc1.'))
        return [mapped];

    return getAvcCandidates(mapped);
}

// Only ever widens: a decoder configured for a higher profile or level decodes
// everything below it, while declaring less than the bitstream carries makes
// configure() succeed and decode() drop chunks silently. So the fallbacks are
// the same profile at the top level, then High at the top level.
function getAvcCandidates(codec: string): string[] {
    const match = /^avc1\.([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(codec);
    if (!match)
        return [codec];

    const [, profile, constraints] = match;
    const topLevel = '34'; // L5.2 — the highest level our ladders ever declare
    const candidates = [
        codec,
        `avc1.${profile}${constraints}${topLevel}`,
        `avc1.6400${topLevel}`,
    ];

    return candidates.filter((c, i) => candidates.indexOf(c) === i);
}

// avcC bytes 1..3 are profile_idc, profile_compatibility and level_idc.
function avcCodecStringFromDescription(description: ArrayBuffer): string {
    const bytes = new Uint8Array(description);
    const hex = (b: number): string => b.toString(16).padStart(2, '0');

    return `avc1.${hex(bytes[1])}${hex(bytes[2])}${hex(bytes[3])}`;
}

function replaceHevcTier(codec: string, tier: 'H' | 'L'): string {
    return codec.replace(/\.([HL])(\d+)(?=\.|$)/i, `.${tier}$2`);
}

// Probe each candidate against isConfigSupported with the FULL config —
// codec-string-only probes return false positives (e.g. Edge accepts
// hev1.1.6.H120.90 by string but rejects on configure() when HVCC says tier=L).
export async function selectDecoderCodec(
    candidates: string[],
    description: ArrayBuffer | undefined,
    dimensions?: DecoderDimensions,
    excluded?: ReadonlySet<string>,
): Promise<DecoderCodecSelection | null> {
    for (const hardwareAcceleration of ['prefer-hardware', 'no-preference'] as const) {
        for (const candidate of candidates) {
            if (excluded?.has(candidate))
                continue;

            try {
                const config: VideoDecoderConfig = {
                    codec: candidate,
                    hardwareAcceleration,
                };
                if (description) config.description = description;
                if (dimensions) {
                    config.codedWidth = dimensions.width;
                    config.codedHeight = dimensions.height;
                }
                const { supported } = await VideoDecoder.isConfigSupported(config);
                if (supported)
                    return { codec: candidate, hardwareAcceleration };
            } catch { /* continue to next candidate */ }
        }
    }

    return null;
}

// Walk HVCC NAL arrays, locate SPS, return tier_flag from profile_tier_level.
// Returns -1 if not found/malformed. Workaround for Chrome encoder bug: writes
// SPS tier=High while HVCC byte[1] says tier=Low — iOS HEVC HW rejects, Edge
// silently stalls.
export function extractHvccSpsTierFlag(bytes: Uint8Array): number {
    if (bytes.length < 24)
        return -1;

    const numArrays = bytes[22];
    let pos = 23;
    for (let i = 0; i < numArrays; i++) {
        if (pos >= bytes.length)
            return -1;

        const nalUnitType = bytes[pos] & 0x3F;
        pos += 1;
        if (pos + 2 > bytes.length)
            return -1;

        const numNalus = (bytes[pos] << 8) | bytes[pos + 1];
        pos += 2;
        for (let j = 0; j < numNalus; j++) {
            if (pos + 2 > bytes.length)
                return -1;

            const nalLen = (bytes[pos] << 8) | bytes[pos + 1];
            pos += 2;
            if (pos + nalLen > bytes.length)
                return -1;
            if (nalUnitType === 33 && nalLen >= 4) {
                // SPS layout: 2-byte NAL hdr, then RBSP. RBSP byte 1 high bit
                // group: general_profile_space<<6 | general_tier_flag<<5 | profile_idc.
                return (bytes[pos + 3] >> 5) & 0x01;
            }
            pos += nalLen;
        }
    }
    return -1;
}

// Spec-compliant HEVC codec string from HVCC bytes (all fields read, none
// hardcoded). Output: {prefix}.{profileSpace?}{profileIdc}.{compatHexReversed}
// .{L|H}{levelIdc}[.{constraintBytes…}]. tierOverride lets caller force H/L;
// default reads HVCC byte[1]. Spec: ISO/IEC 14496-15 §8.3.3.1 + RFC 7798 §7.1.
export function deriveHevcCodecString(prefix: 'hev1' | 'hvc1', bytes: Uint8Array, tierOverride?: 'H' | 'L'): string {
    const profileSpace = (bytes[1] >> 6) & 0x03;
    const tierFlag = (bytes[1] >> 5) & 0x01;
    const profileIdc = bytes[1] & 0x1F;
    const levelIdc = bytes[12];

    // 32-bit compat flags (BE) then full bit-reversal — codec string format
    // expresses flag bit 0 as the encoded int's LSB.
    let compat = (((bytes[2] << 24) >>> 0) | (bytes[3] << 16) | (bytes[4] << 8) | bytes[5]) >>> 0;
    compat = (((compat & 0xAAAAAAAA) >>> 1) | ((compat & 0x55555555) << 1)) >>> 0;
    compat = (((compat & 0xCCCCCCCC) >>> 2) | ((compat & 0x33333333) << 2)) >>> 0;
    compat = (((compat & 0xF0F0F0F0) >>> 4) | ((compat & 0x0F0F0F0F) << 4)) >>> 0;
    compat = (((compat & 0xFF00FF00) >>> 8) | ((compat & 0x00FF00FF) << 8)) >>> 0;
    compat = ((compat >>> 16) | (compat << 16)) >>> 0;

    const profileStr = profileSpace > 0
        ? String.fromCharCode(0x40 + profileSpace) + profileIdc.toString()
        : profileIdc.toString();
    const tierStr = tierOverride ?? (tierFlag ? 'H' : 'L');
    const compatHex = compat.toString(16).toUpperCase();

    // 6 constraint bytes from HVCC byte 6, MSB-first, dot-separated hex;
    // trailing zero bytes stripped per codec-string convention.
    let lastNonZero = -1;
    for (let i = 0; i < 6; i++) {
        if (bytes[6 + i] !== 0) lastNonZero = i;
    }
    let constraintSuffix = '';
    if (lastNonZero >= 0) {
        const parts: string[] = [];
        for (let i = 0; i <= lastNonZero; i++) {
            parts.push(bytes[6 + i].toString(16).toUpperCase().padStart(2, '0'));
        }
        constraintSuffix = '.' + parts.join('.');
    }

    return `${prefix}.${profileStr}.${compatHex}.${tierStr}${levelIdc}${constraintSuffix}`;
}

// HVCC layout (ISO 14496-15): byte[0]=version=1, byte[1]=profileSpace<<6|tier<<5|
// profileIdc, byte[2..5]=compatFlags, byte[6..11]=constraintFlags, byte[12]=levelIdc.
// Min 23 bytes before nalu arrays.
export function isHvccDescription(description: ArrayBuffer): boolean {
    if (description.byteLength < 23)
        return false;

    const bytes = new Uint8Array(description);
    if (bytes[0] !== 0x01)
        return false;

    // Valid HEVC profiles: 1 Main, 2 Main10, 3 MainStillPicture, 4 RangeExt, 5 HighThroughput.
    const generalProfileIdc = bytes[1] & 0x1F;
    if (generalProfileIdc === 0 || generalProfileIdc > 11)
        return false;

    // Negative discriminator vs avcC: avcC byte[4] has top 6 bits set
    // (0xFC|lengthSizeMinusOne) and byte[5] has top 3 bits set (0xE0|numSPS).
    // HVCC has no such constraint at those positions.
    if ((bytes[4] & 0xFC) === 0xFC && (bytes[5] & 0xE0) === 0xE0)
        return false;

    const generalLevelIdc = bytes[12];
    if (generalLevelIdc === 0)
        return false;

    return true;
}

export function isAvcCDescription(description: ArrayBuffer): boolean {
    if (description.byteLength < 7)
        return false;

    const bytes = new Uint8Array(description);
    if (bytes[0] !== 0x01)
        return false;

    const validProfiles = [66, 77, 88, 100, 110, 122, 244];
    if (!validProfiles.includes(bytes[1]))
        return false;

    // byte[4] = 0xFC | lengthSizeMinusOne (top 6 bits set).
    if ((bytes[4] & 0xFC) !== 0xFC)
        return false;

    // byte[5] = 0xE0 | numOfSequenceParameterSets (top 3 bits set).
    if ((bytes[5] & 0xE0) !== 0xE0)
        return false;

    return true;
}

// Maps non-HEVC codec name + optional description to a WebCodecs codec string.
export function mapCodecToWebCodecs(codec: string, description?: ArrayBuffer): string {
    if (description && description.byteLength >= 5 && isAvcCDescription(description)) {
        const codecString = avcCodecStringFromDescription(description);
        const declaredLower = codec.toLowerCase();
        if (declaredLower !== 'h264' && declaredLower !== 'avc1' && !declaredLower.startsWith('avc1.'))
            warnLog?.log(`Codec mismatch: declared=${codec} but description is avcC, overriding to ${codecString}`);

        return codecString;
    }

    const lower = codec.toLowerCase();
    if (description && description.byteLength >= 4 && (lower === 'h264' || lower === 'avc1'))
        return avcCodecStringFromDescription(description);

    const codecMap: Record<string, string> = {
        'h264': 'avc1.42E01F',
        'avc1': 'avc1.42E01F',
        'h265': 'hvc1.1.6.L93.90',
        'hevc': 'hvc1.1.6.L93.90',
        'vp8': 'vp8',
        'vp9': 'vp09.00.31.08',
        'av1': 'av01.0.01M.08',
    };

    const lowerCodec = codec.toLowerCase();
    if (codecMap[lowerCodec])
        return codecMap[lowerCodec];
    if (codec.includes('.'))
        return codec;

    // Constrained Baseline: the most portable profile, and the right guess for
    // a stream that never said what it is.
    return 'avc1.42E01F';
}
