/**
 * HEVC/H.265 NALU Parser and HVCC Builder
 * Parses H.265 bitstream NAL units and constructs HVCC (HEVC Decoder Configuration Record)
 * according to ISO/IEC 14496-15
 */

export interface HEVCParameterSets {
  vps: Uint8Array[];  // Video Parameter Sets (NAL type 32)
  sps: Uint8Array[];  // Sequence Parameter Sets (NAL type 33)
  pps: Uint8Array[];  // Picture Parameter Sets (NAL type 34)
}

export interface HEVCDecoderConfigurationRecord {
  configurationVersion: number;
  generalProfileSpace: number;
  generalTierFlag: number;
  generalProfileIdc: number;
  generalProfileCompatibilityFlags: number;
  generalConstraintIndicatorFlags: bigint;
  generalLevelIdc: number;
  minSpatialSegmentationIdc: number;
  parallelismType: number;
  chromaFormatIdc: number;
  bitDepthLumaMinus8: number;
  bitDepthChromaMinus8: number;
  avgFrameRate: number;
  constantFrameRate: number;
  numTemporalLayers: number;
  temporalIdNested: number;
  lengthSizeMinusOne: number;
  numOfArrays: number;
  arrays: {
    arrayCompleteness: number;
    nalUnitType: number;
    numNalus: number;
    nalUnits: Uint8Array[];
  }[];
}

/**
 * Parse HEVC NAL units from an EncodedVideoChunk
 */
export function parseHEVCNALUs(chunk: EncodedVideoChunk): HEVCParameterSets {
    const buffer = new Uint8Array(chunk.byteLength);
    chunk.copyTo(buffer);
  
    const parameterSets: HEVCParameterSets = {
        vps: [],
        sps: [],
        pps: []
    };
  
    // Find NAL units using start codes (0x00 0x00 0x00 0x01 or 0x00 0x00 0x01)
    let i = 0;
    while (i < buffer.length - 4) {
    // Look for 4-byte start code
        if (buffer[i] === 0x00 && buffer[i + 1] === 0x00 && 
        buffer[i + 2] === 0x00 && buffer[i + 3] === 0x01) {
      
            const nalStart = i + 4;
            i += 4;
      
            // Find next start code
            let nalEnd = buffer.length;
            for (let j = i; j < buffer.length - 3; j++) {
                if ((buffer[j] === 0x00 && buffer[j + 1] === 0x00 && buffer[j + 2] === 0x01) ||
            (buffer[j] === 0x00 && buffer[j + 1] === 0x00 && 
             buffer[j + 2] === 0x00 && buffer[j + 3] === 0x01)) {
                    nalEnd = j;
                    break;
                }
            }
      
            if (nalStart < nalEnd) {
                const nalUnit = buffer.slice(nalStart, nalEnd);
                const nalUnitType = (nalUnit[0] >> 1) & 0x3F;
        
                // VPS = 32, SPS = 33, PPS = 34
                if (nalUnitType === 32) {
                    parameterSets.vps.push(nalUnit);
                } else if (nalUnitType === 33) {
                    parameterSets.sps.push(nalUnit);
                } else if (nalUnitType === 34) {
                    parameterSets.pps.push(nalUnit);
                }
            }
      
            i = nalEnd;
        } else if (buffer[i] === 0x00 && buffer[i + 1] === 0x00 && buffer[i + 2] === 0x01) {
            // 3-byte start code
            const nalStart = i + 3;
            i += 3;
      
            let nalEnd = buffer.length;
            for (let j = i; j < buffer.length - 2; j++) {
                if ((buffer[j] === 0x00 && buffer[j + 1] === 0x00 && buffer[j + 2] === 0x01) ||
            (j < buffer.length - 3 && buffer[j] === 0x00 && buffer[j + 1] === 0x00 && 
             buffer[j + 2] === 0x00 && buffer[j + 3] === 0x01)) {
                    nalEnd = j;
                    break;
                }
            }
      
            if (nalStart < nalEnd) {
                const nalUnit = buffer.slice(nalStart, nalEnd);
                const nalUnitType = (nalUnit[0] >> 1) & 0x3F;
        
                if (nalUnitType === 32) {
                    parameterSets.vps.push(nalUnit);
                } else if (nalUnitType === 33) {
                    parameterSets.sps.push(nalUnit);
                } else if (nalUnitType === 34) {
                    parameterSets.pps.push(nalUnit);
                }
            }
      
            i = nalEnd;
        } else {
            i++;
        }
    }
  
    return parameterSets;
}

/**
 * Parse SPS to extract profile/level information
 */
function parseSPS(sps: Uint8Array): Partial<HEVCDecoderConfigurationRecord> {
    // Skip NAL header (2 bytes)
    let bitPos = 16;
  
    // Helper to read bits
    const readBits = (n: number): number => {
        let value = 0;
        for (let i = 0; i < n; i++) {
            const bytePos = Math.floor(bitPos / 8);
            const bitOffset = 7 - (bitPos % 8);
            value = (value << 1) | ((sps[bytePos] >> bitOffset) & 1);
            bitPos++;
        }
        return value;
    };
  
    // Read UE(v) - unsigned exp-golomb
    const readUE = (): number => {
        let leadingZeros = 0;
        while (readBits(1) === 0) leadingZeros++;
        if (leadingZeros === 0) return 0;
        return (1 << leadingZeros) - 1 + readBits(leadingZeros);
    };
  
    try {
    // sps_video_parameter_set_id
        readBits(4);
    
        // sps_max_sub_layers_minus1
        const maxSubLayersMinus1 = readBits(3);
    
        // sps_temporal_id_nesting_flag
        const temporalIdNested = readBits(1);
    
        // profile_tier_level()
        const generalProfileSpace = readBits(2);
        const generalTierFlag = readBits(1);
        const generalProfileIdc = readBits(5);
    
        // general_profile_compatibility_flag[32]
        const generalProfileCompatibilityFlags = readBits(32);
    
        // general_constraint_indicator_flags[48 bits]
        const constraintHigh = readBits(32);
        const constraintLow = readBits(16);
        const generalConstraintIndicatorFlags = (BigInt(constraintHigh) << 16n) | BigInt(constraintLow);
    
        // general_level_idc
        const generalLevelIdc = readBits(8);
    
        // Skip sub-layer profile/level info
        // ...
    
        // sps_seq_parameter_set_id
        readUE();
    
        // chroma_format_idc
        const chromaFormatIdc = readUE();
    
        let bitDepthLumaMinus8 = 0;
        let bitDepthChromaMinus8 = 0;
    
        if (chromaFormatIdc === 3) {
            // separate_colour_plane_flag
            readBits(1);
        }
    
        // pic_width_in_luma_samples
        readUE();
        // pic_height_in_luma_samples
        readUE();
    
        // conformance_window_flag
        if (readBits(1)) {
            readUE(); // conf_win_left_offset
            readUE(); // conf_win_right_offset
            readUE(); // conf_win_top_offset
            readUE(); // conf_win_bottom_offset
        }
    
        bitDepthLumaMinus8 = readUE();
        bitDepthChromaMinus8 = readUE();
    
        return {
            generalProfileSpace,
            generalTierFlag,
            generalProfileIdc,
            generalProfileCompatibilityFlags,
            generalConstraintIndicatorFlags,
            generalLevelIdc,
            chromaFormatIdc,
            bitDepthLumaMinus8,
            bitDepthChromaMinus8,
            numTemporalLayers: maxSubLayersMinus1 + 1,
            temporalIdNested
        };
    } catch (error) {
        console.error('Error parsing SPS:', error);
        // Return defaults
        return {
            generalProfileSpace: 0,
            generalTierFlag: 0,
            generalProfileIdc: 1,
            generalProfileCompatibilityFlags: 0,
            generalConstraintIndicatorFlags: 0n,
            generalLevelIdc: 93,
            chromaFormatIdc: 1,
            bitDepthLumaMinus8: 0,
            bitDepthChromaMinus8: 0,
            numTemporalLayers: 1,
            temporalIdNested: 1
        };
    }
}

/**
 * Build HVCC (HEVC Decoder Configuration Record) from parameter sets
 */
export function buildHVCC(parameterSets: HEVCParameterSets): Uint8Array {
    // Parse SPS to get profile/level info
    const spsInfo = parameterSets.sps.length > 0 
        ? parseSPS(parameterSets.sps[0]) 
        : {};
  
    // Calculate total size
    let totalSize = 23; // Fixed header size
  
    // Size for NAL unit arrays
    const arrays: { type: number; nalUnits: Uint8Array[] }[] = [];
  
    if (parameterSets.vps.length > 0) {
        arrays.push({ type: 32, nalUnits: parameterSets.vps });
        totalSize += 3; // array header
        parameterSets.vps.forEach(vps => totalSize += 2 + vps.length);
    }
  
    if (parameterSets.sps.length > 0) {
        arrays.push({ type: 33, nalUnits: parameterSets.sps });
        totalSize += 3;
        parameterSets.sps.forEach(sps => totalSize += 2 + sps.length);
    }
  
    if (parameterSets.pps.length > 0) {
        arrays.push({ type: 34, nalUnits: parameterSets.pps });
        totalSize += 3;
        parameterSets.pps.forEach(pps => totalSize += 2 + pps.length);
    }
  
    const hvcc = new Uint8Array(totalSize);
    let offset = 0;
  
    // Write HVCC header
    hvcc[offset++] = 1; // configurationVersion
  
    // Profile/tier/level
    hvcc[offset++] = ((spsInfo.generalProfileSpace || 0) << 6) | 
                   ((spsInfo.generalTierFlag || 0) << 5) | 
                   (spsInfo.generalProfileIdc || 1);
  
    // general_profile_compatibility_flags (4 bytes)
    const compat = spsInfo.generalProfileCompatibilityFlags || 0;
    hvcc[offset++] = (compat >> 24) & 0xFF;
    hvcc[offset++] = (compat >> 16) & 0xFF;
    hvcc[offset++] = (compat >> 8) & 0xFF;
    hvcc[offset++] = compat & 0xFF;
  
    // general_constraint_indicator_flags (6 bytes)
    const constraint = spsInfo.generalConstraintIndicatorFlags || 0n;
    hvcc[offset++] = Number((constraint >> 40n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 32n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 24n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 16n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 8n) & 0xFFn);
    hvcc[offset++] = Number(constraint & 0xFFn);
  
    hvcc[offset++] = spsInfo.generalLevelIdc || 93; // general_level_idc
  
    // min_spatial_segmentation_idc (12 bits) + reserved (4 bits)
    hvcc[offset++] = 0xF0;
    hvcc[offset++] = 0x00;
  
    // parallelismType (2 bits) + reserved (6 bits)
    hvcc[offset++] = 0xFC;
  
    // chromaFormat (2 bits) + reserved (6 bits)
    hvcc[offset++] = 0xFC | ((spsInfo.chromaFormatIdc || 1) & 0x03);
  
    // bitDepthLumaMinus8 (3 bits) + reserved (5 bits)
    hvcc[offset++] = 0xF8 | ((spsInfo.bitDepthLumaMinus8 || 0) & 0x07);
  
    // bitDepthChromaMinus8 (3 bits) + reserved (5 bits)
    hvcc[offset++] = 0xF8 | ((spsInfo.bitDepthChromaMinus8 || 0) & 0x07);
  
    // avgFrameRate (16 bits)
    hvcc[offset++] = 0x00;
    hvcc[offset++] = 0x00;
  
    // constantFrameRate (2 bits) + numTemporalLayers (3 bits) + 
    // temporalIdNested (1 bit) + lengthSizeMinusOne (2 bits)
    hvcc[offset++] = (0 << 6) | // constantFrameRate
                   (((spsInfo.numTemporalLayers || 1) & 0x07) << 3) |
                   (((spsInfo.temporalIdNested || 1) & 0x01) << 2) |
                   3; // lengthSizeMinusOne = 3 (4-byte length)
  
    // numOfArrays
    hvcc[offset++] = arrays.length;
  
    // Write NAL unit arrays
    for (const array of arrays) {
    // array_completeness (1 bit) + reserved (1 bit) + NAL_unit_type (6 bits)
        hvcc[offset++] = 0x80 | (array.type & 0x3F);
    
        // numNalus (16 bits)
        hvcc[offset++] = (array.nalUnits.length >> 8) & 0xFF;
        hvcc[offset++] = array.nalUnits.length & 0xFF;
    
        // Write each NAL unit
        for (const nalUnit of array.nalUnits) {
            // nalUnitLength (16 bits)
            hvcc[offset++] = (nalUnit.length >> 8) & 0xFF;
            hvcc[offset++] = nalUnit.length & 0xFF;
      
            // nalUnit data
            hvcc.set(nalUnit, offset);
            offset += nalUnit.length;
        }
    }
  
    return hvcc;
}

/**
 * Extract HVCC from an encoded HEVC chunk
 */
export function extractHVCC(chunk: EncodedVideoChunk): Uint8Array | null {
    try {
        const parameterSets = parseHEVCNALUs(chunk);
    
        // Need at least one SPS to build valid HVCC
        if (parameterSets.sps.length === 0) {
            console.warn('[HEVC Parser] No SPS found in chunk, cannot build HVCC');
            return null;
        }
    
        const hvcc = buildHVCC(parameterSets);
        console.log('[HEVC Parser] Built HVCC:', {
            size: hvcc.length,
            vps: parameterSets.vps.length,
            sps: parameterSets.sps.length,
            pps: parameterSets.pps.length
        });
    
        return hvcc;
    } catch (error) {
        console.error('[HEVC Parser] Error extracting HVCC:', error);
        return null;
    }
}