/**
 * HEVC codec-string derivation + WebCodecs decoder candidate selection.
 *
 * Pure helpers split out of `VideoPlayer` so the decoder worker can re-derive
 * codec strings when the keyframe description changes mid-stream (e.g.
 * simulcast layer switch).
 *
 * Selection rule: hardware acceleration only. If no candidate is HW-supported
 * for the given description, the helper returns null — callers surface a
 * stream-level failure rather than silently falling through to software.
 * (Project policy: feedback_no_software_codec_fallback.)
 */

import { getLogs } from 'logging';

const { warnLog } = getLogs('VideoDecoder');

export interface DecoderDimensions {
    width: number;
    height: number;
}

export interface DecoderCodecSelection {
    codec: string;
    hardwareAcceleration: 'prefer-hardware';
}

/**
 * Build an ordered list of codec string candidates for decoder configuration.
 * For HEVC: tries high-enough candidates using the SPS tier (ground truth from
 * bitstream), then SPS-derived, HVCC header, opposite-tier, and declared
 * fallbacks.
 * For other codecs: returns a single candidate from the legacy mapping.
 */
export function getCodecCandidates(codec: string, description?: ArrayBuffer): string[] {
    if (description && description.byteLength >= 5 && isHvccDescription(description)) {
        const bytes = new Uint8Array(description);
        const generalProfileIdc = bytes[1] & 0x1F;
        const tier = (bytes[1] >> 5) & 0x01;
        const tierStr = tier ? 'H' : 'L';

        const candidates: string[] = [];
        const seen = new Set<string>();
        const addCandidate = (c: string) => {
            if (!seen.has(c)) {
                seen.add(c);
                candidates.push(c);
            }
        };

        // Ordering rationale:
        //   In simulcast each layer ships its own HVCC, and the per-layer
        //   `level_idc` reflects that layer's dims (e.g. 360p base → L63
        //   = Level 2.1). Configuring the decoder with a low level
        //   succeeds (Chrome's `isConfigSupported` does NOT cross-check
        //   codec-string level vs description level), but `decode()` then
        //   silently drops chunks whose bitstream level exceeds the
        //   declared one — surfacing as `0 frames decoded` and a stuck
        //   `<video>`. Always probe a high-enough level first so the chosen
        //   codec admits the entire ladder.
        //
        //   Tier still has to come from SPS when available. Chrome's HEVC
        //   encoder can write HVCC tier=Low while the SPS says tier=High; iOS
        //   HEVC HW rejects that mismatch and Edge/Chrome HW can silently stall.
        //   So the first candidates keep the larger declared/default level but
        //   normalize tier to SPS.
        const spsTier = extractHvccSpsTierFlag(bytes);
        const spsTierStr: 'H' | 'L' | undefined = spsTier === 1 ? 'H' : (spsTier === 0 ? 'L' : undefined);
        const preferredTier = spsTierStr ?? tierStr;

        // 1. Sender's declared codec string carries the ladder-top level; use
        //    it only after reconciling tier against SPS.
        const lc = codec.toLowerCase();
        if (lc.startsWith('hev1.') || lc.startsWith('hvc1.')) {
            addCandidate(replaceHevcTier(codec, preferredTier));
        }

        // 2. Hardcoded Level 4.0 fallback (admits up to 1080p), still using
        //    the SPS tier when available.
        addCandidate(`hev1.${generalProfileIdc}.6.${preferredTier}120.B0`);
        addCandidate(`hvc1.${generalProfileIdc}.6.${preferredTier}120.B0`);

        // 3. Derived from HVCC binary description (ground truth for THIS
        //    layer) — both hev1 and hvc1 prefixes.
        if (spsTierStr) {
            addCandidate(deriveHevcCodecString('hev1', bytes, spsTierStr));
            addCandidate(deriveHevcCodecString('hvc1', bytes, spsTierStr));
        }
        // Fallbacks built off HVCC header (legacy path).
        addCandidate(deriveHevcCodecString('hev1', bytes));
        addCandidate(deriveHevcCodecString('hvc1', bytes));
        // Belt-and-suspenders: the opposite tier too, in case neither HVCC nor
        // SPS detection landed on the right answer.
        addCandidate(deriveHevcCodecString('hev1', bytes, 'H'));
        addCandidate(deriveHevcCodecString('hvc1', bytes, 'H'));
        addCandidate(deriveHevcCodecString('hev1', bytes, 'L'));
        addCandidate(deriveHevcCodecString('hvc1', bytes, 'L'));
        if (lc.startsWith('hev1.') || lc.startsWith('hvc1.'))
            addCandidate(codec);

        const declaredLower = codec.toLowerCase();
        if (!declaredLower.startsWith('hev1') && !declaredLower.startsWith('hvc1')
            && declaredLower !== 'hevc' && declaredLower !== 'h265') {
            warnLog?.log(`Codec mismatch: declared=${codec} but description is HVCC`);
        }

        return candidates;
    }

    // Non-HEVC codecs: single candidate from legacy mapping
    return [mapCodecToWebCodecs(codec, description)];
}

function replaceHevcTier(codec: string, tier: 'H' | 'L'): string {
    return codec.replace(/\.([HL])(\d+)(?=\.|$)/i, `.${tier}$2`);
}

/**
 * Probe each candidate against `VideoDecoder.isConfigSupported` with the FULL
 * config (codec + description + dimensions + HW preference). Returns the first
 * HW-supported candidate, or null if none match.
 *
 * Carrying `description` and dimensions in the probe is critical: codec-string-
 * only probes return false positives (e.g. Edge accepts `hev1.1.6.H120.90` by
 * codec string but rejects it on `configure()` when the HVCC bytes say tier=L).
 *
 * `excluded` lets callers skip candidates already known to fail (e.g. recovery
 * after a configure() error on the previously-selected codec).
 */
export async function selectDecoderCodec(
    candidates: string[],
    description: ArrayBuffer | undefined,
    dimensions?: DecoderDimensions,
    excluded?: ReadonlySet<string>,
): Promise<DecoderCodecSelection | null> {
    for (const candidate of candidates) {
        if (excluded?.has(candidate)) continue;
        try {
            const config: VideoDecoderConfig = {
                codec: candidate,
                hardwareAcceleration: 'prefer-hardware',
            };
            if (description) config.description = description;
            if (dimensions) {
                config.codedWidth = dimensions.width;
                config.codedHeight = dimensions.height;
            }
            const { supported } = await VideoDecoder.isConfigSupported(config);
            if (supported) {
                return { codec: candidate, hardwareAcceleration: 'prefer-hardware' };
            }
        } catch { /* continue to next candidate */ }
    }
    return null;
}

// Walk HVCC NAL arrays, locate the SPS, return its tier_flag from
// profile_tier_level. Returns -1 if not found / malformed.
// Encoder bug: Chrome HEVC encoder produces SPS with tier=High while writing
// tier=Low in the HVCC header byte[1] — decoders that pick tier from HVCC
// and compare against SPS reject (iOS) or silently stall (Edge/Chrome HW).
export function extractHvccSpsTierFlag(bytes: Uint8Array): number {
    if (bytes.length < 24) return -1;
    const numArrays = bytes[22];
    let pos = 23;
    for (let i = 0; i < numArrays; i++) {
        if (pos >= bytes.length) return -1;
        const nalUnitType = bytes[pos] & 0x3F;
        pos += 1;
        if (pos + 2 > bytes.length) return -1;
        const numNalus = (bytes[pos] << 8) | bytes[pos + 1];
        pos += 2;
        for (let j = 0; j < numNalus; j++) {
            if (pos + 2 > bytes.length) return -1;
            const nalLen = (bytes[pos] << 8) | bytes[pos + 1];
            pos += 2;
            if (pos + nalLen > bytes.length) return -1;
            if (nalUnitType === 33 && nalLen >= 4) {
                // SPS NAL layout: 2-byte NAL header, then RBSP.
                // RBSP byte 0 = sps_video_parameter_set_id<<4 | max_sub_layers_minus1<<1 | temporal_id_nesting_flag
                // RBSP byte 1 = general_profile_space<<6 | general_tier_flag<<5 | general_profile_idc
                return (bytes[pos + 3] >> 5) & 0x01;
            }
            pos += nalLen;
        }
    }
    return -1;
}

// Build a fully spec-compliant HEVC codec string from HVCC bytes — every
// field is read from the description, no hardcoded suffixes. Output:
//   {prefix}.{profileSpace?}{profileIdc}.{compatHexReversed}.{L|H}{levelIdc}[.{constraintByte0}[.{constraintByte1}…]]
// For our typical encoder output (Main L4.0, constraint=0x90 …) this yields
// `hev1.1.6.L120.90` rather than the legacy hardcoded `hev1.1.6.L120.B0`.
// `tierOverride` lets the caller force `H` or `L`; default reads HVCC byte[1].
// Spec: ISO/IEC 14496-15 §8.3.3.1 + RFC 7798 §7.1.
export function deriveHevcCodecString(prefix: 'hev1' | 'hvc1', bytes: Uint8Array, tierOverride?: 'H' | 'L'): string {
    const profileSpace = (bytes[1] >> 6) & 0x03;
    const tierFlag = (bytes[1] >> 5) & 0x01;
    const profileIdc = bytes[1] & 0x1F;
    const levelIdc = bytes[12];

    // 32-bit profile compat flags (BE), then full 32-bit bit reversal — codec
    // string format expresses bit 0 of the flags as the LSB of the encoded int.
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

    // 6 constraint bytes starting at HVCC byte 6, MSB-first, dot-separated
    // hex; trailing zero bytes are stripped (codec-string convention).
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

/**
 * Detect HEVC HVCC (HEVCDecoderConfigurationRecord) format.
 * HVCC structure (ISO 14496-15):
 *   byte[0]  = configurationVersion (must be 1)
 *   byte[1]  = general_profile_space(2) | general_tier_flag(1) | general_profile_idc(5)
 *   byte[2..5]  = general_profile_compatibility_flags (4 bytes)
 *   byte[6..11] = general_constraint_indicator_flags (6 bytes)
 *   byte[12] = general_level_idc
 *   ...minimum 23 bytes total before nalu arrays
 */
export function isHvccDescription(description: ArrayBuffer): boolean {
    // HVCC minimum size is 23 bytes (header before nalu arrays)
    if (description.byteLength < 23) return false;
    const bytes = new Uint8Array(description);
    // configurationVersion must be 1
    if (bytes[0] !== 0x01) return false;
    // general_profile_idc: valid HEVC profiles are 1 (Main), 2 (Main10), 3 (MainStillPicture), 4 (Range Extensions), 5 (High Throughput)
    const generalProfileIdc = bytes[1] & 0x1F;
    if (generalProfileIdc === 0 || generalProfileIdc > 11) return false;
    // Discriminator vs avcC: in avcC byte[5] = (0xE0 | numSPS) where top 3 bits are always 1
    // In HVCC byte[5] is part of general_profile_compatibility_flags — no such constraint
    // Also in avcC byte[4] = (0xFC | lengthSizeMinusOne) where top 6 bits are always 1
    // In HVCC byte[4] is part of general_profile_compatibility_flags — no such constraint
    // Use the avcC byte[5] marker as negative discriminator:
    // If byte[4] has top 6 bits set AND byte[5] has top 3 bits set, this looks like avcC, not HVCC
    if ((bytes[4] & 0xFC) === 0xFC && (bytes[5] & 0xE0) === 0xE0) return false;
    // general_level_idc at byte[12] should be reasonable (30-186 for common HEVC levels)
    const generalLevelIdc = bytes[12];
    if (generalLevelIdc === 0) return false;
    return true;
}

/**
 * Detect H.264 avcC (AVCDecoderConfigurationRecord) format.
 */
export function isAvcCDescription(description: ArrayBuffer): boolean {
    if (description.byteLength < 7) return false;
    const bytes = new Uint8Array(description);
    // avcC configurationVersion must be 1
    if (bytes[0] !== 0x01) return false;
    // byte[1] = AVCProfileIndication — valid H.264 profiles
    const validProfiles = [66, 77, 88, 100, 110, 122, 244];
    if (!validProfiles.includes(bytes[1])) return false;
    // byte[4] = 0xFC | (lengthSizeMinusOne & 0x03) — top 6 bits must be set
    if ((bytes[4] & 0xFC) !== 0xFC) return false;
    // byte[5] = 0xE0 | (numOfSequenceParameterSets & 0x1F) — top 3 bits must be set
    if ((bytes[5] & 0xE0) !== 0xE0) return false;
    return true;
}

/**
 * Map a codec name + optional description to a WebCodecs codec string for
 * non-HEVC codecs. Used by `getCodecCandidates` for the non-HEVC path and by
 * the main-thread RPC fallback in `VideoPlayer.pushFrame` to re-derive the
 * codec string when a new keyframe description arrives.
 */
export function mapCodecToWebCodecs(codec: string, description?: ArrayBuffer): string {
    // Derive H.264 codec string from avcC description bytes
    if (description && description.byteLength >= 5 && isAvcCDescription(description)) {
        const bytes = new Uint8Array(description);
        const profileIndication = bytes[1];
        const profileCompatibility = bytes[2];
        const levelIndication = bytes[3];
        const codecString = `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
        const declaredLower = codec.toLowerCase();
        if (declaredLower !== 'h264' && declaredLower !== 'avc1' && !declaredLower.startsWith('avc1.')) {
            warnLog?.log(`Codec mismatch: declared=${codec} but description is avcC, overriding to ${codecString}`);
        }
        return codecString;
    }

    // If we have an avcC description and declared H.264, extract the actual profile
    if (description && description.byteLength >= 4 && (codec.toLowerCase() === 'h264' || codec.toLowerCase() === 'avc1')) {
        const bytes = new Uint8Array(description);
        const profileIndication = bytes[1];
        const profileCompatibility = bytes[2];
        const levelIndication = bytes[3];
        return `avc1.${profileIndication.toString(16).padStart(2, '0')}${profileCompatibility.toString(16).padStart(2, '0')}${levelIndication.toString(16).padStart(2, '0')}`;
    }

    // Map common codec names to WebCodecs codec strings
    const codecMap: Record<string, string> = {
        'h264': 'avc1.640028',
        'avc1': 'avc1.640028',
        'h265': 'hvc1.1.6.L93.90',
        'hevc': 'hvc1.1.6.L93.90',
        'vp8': 'vp8',
        'vp9': 'vp09.00.31.08',
        'av1': 'av01.0.01M.08',
    };

    const lowerCodec = codec.toLowerCase();
    if (codecMap[lowerCodec]) {
        return codecMap[lowerCodec];
    }
    if (codec.includes('.')) {
        return codec;
    }
    return 'avc1.640028';
}
