// HEVC/H.265 NALU parser and HVCC builder (HEVC Decoder Configuration Record,
// ISO/IEC 14496-15).

import { getLogs } from 'logging';

const { infoLog, warnLog, errorLog } = getLogs('VideoDecoder');

export interface HEVCParameterSets {
  vps: Uint8Array[];  // NAL type 32
  sps: Uint8Array[];  // NAL type 33
  pps: Uint8Array[];  // NAL type 34
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

export function parseHEVCNALUs(chunk: EncodedVideoChunk): HEVCParameterSets {
    const buffer = new Uint8Array(chunk.byteLength);
    chunk.copyTo(buffer);
  
    const parameterSets: HEVCParameterSets = {
        vps: [],
        sps: [],
        pps: []
    };
  
    // NAL start codes: 4-byte (00 00 00 01) or 3-byte (00 00 01).
    let i = 0;
    while (i < buffer.length - 4) {
        if (buffer[i] === 0x00 && buffer[i + 1] === 0x00 &&
        buffer[i + 2] === 0x00 && buffer[i + 3] === 0x01) {

            const nalStart = i + 4;
            i += 4;

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

function parseSPS(sps: Uint8Array): Partial<HEVCDecoderConfigurationRecord> {
    let bitPos = 16; // skip 2-byte NAL header

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

    // unsigned exp-golomb
    const readUE = (): number => {
        let leadingZeros = 0;
        while (readBits(1) === 0) leadingZeros++;
        if (leadingZeros === 0) return 0;
        return (1 << leadingZeros) - 1 + readBits(leadingZeros);
    };

    try {
        readBits(4); // sps_video_parameter_set_id
        const maxSubLayersMinus1 = readBits(3);
        const temporalIdNested = readBits(1);

        // profile_tier_level()
        const generalProfileSpace = readBits(2);
        const generalTierFlag = readBits(1);
        const generalProfileIdc = readBits(5);
        const generalProfileCompatibilityFlags = readBits(32);
        const constraintHigh = readBits(32);
        const constraintLow = readBits(16);
        const generalConstraintIndicatorFlags = (BigInt(constraintHigh) << 16n) | BigInt(constraintLow);
        const generalLevelIdc = readBits(8);

        // (sub-layer profile/level info skipped)

        readUE(); // sps_seq_parameter_set_id
        const chromaFormatIdc = readUE();

        let bitDepthLumaMinus8 = 0;
        let bitDepthChromaMinus8 = 0;

        if (chromaFormatIdc === 3) {
            readBits(1); // separate_colour_plane_flag
        }

        readUE(); // pic_width_in_luma_samples
        readUE(); // pic_height_in_luma_samples

        if (readBits(1)) { // conformance_window_flag
            readUE(); readUE(); readUE(); readUE();
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
        errorLog?.log('Error parsing SPS:', error);
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

export function buildHVCC(parameterSets: HEVCParameterSets): Uint8Array {
    const spsInfo = parameterSets.sps.length > 0
        ? parseSPS(parameterSets.sps[0])
        : {};

    let totalSize = 23; // fixed header

    const arrays: { type: number; nalUnits: Uint8Array[] }[] = [];

    if (parameterSets.vps.length > 0) {
        arrays.push({ type: 32, nalUnits: parameterSets.vps });
        totalSize += 3;
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

    hvcc[offset++] = 1; // configurationVersion

    hvcc[offset++] = ((spsInfo.generalProfileSpace ?? 0) << 6) |
                   ((spsInfo.generalTierFlag ?? 0) << 5) |
                   (spsInfo.generalProfileIdc ?? 1);

    const compat = spsInfo.generalProfileCompatibilityFlags ?? 0;
    hvcc[offset++] = (compat >> 24) & 0xFF;
    hvcc[offset++] = (compat >> 16) & 0xFF;
    hvcc[offset++] = (compat >> 8) & 0xFF;
    hvcc[offset++] = compat & 0xFF;

    const constraint = spsInfo.generalConstraintIndicatorFlags ?? 0n;
    hvcc[offset++] = Number((constraint >> 40n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 32n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 24n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 16n) & 0xFFn);
    hvcc[offset++] = Number((constraint >> 8n) & 0xFFn);
    hvcc[offset++] = Number(constraint & 0xFFn);

    hvcc[offset++] = spsInfo.generalLevelIdc ?? 93;

    // min_spatial_segmentation_idc (12) + reserved (4)
    hvcc[offset++] = 0xF0;
    hvcc[offset++] = 0x00;

    // parallelismType (2) + reserved (6)
    hvcc[offset++] = 0xFC;

    // chromaFormat (2) + reserved (6)
    hvcc[offset++] = 0xFC | ((spsInfo.chromaFormatIdc ?? 1) & 0x03);

    // bitDepthLumaMinus8 (3) + reserved (5)
    hvcc[offset++] = 0xF8 | ((spsInfo.bitDepthLumaMinus8 ?? 0) & 0x07);

    // bitDepthChromaMinus8 (3) + reserved (5)
    hvcc[offset++] = 0xF8 | ((spsInfo.bitDepthChromaMinus8 ?? 0) & 0x07);

    // avgFrameRate (16)
    hvcc[offset++] = 0x00;
    hvcc[offset++] = 0x00;

    // constantFrameRate(2) | numTemporalLayers(3) | temporalIdNested(1) | lengthSizeMinusOne(2)
    hvcc[offset++] = (0 << 6) |
                   (((spsInfo.numTemporalLayers ?? 1) & 0x07) << 3) |
                   (((spsInfo.temporalIdNested ?? 1) & 0x01) << 2) |
                   3;

    hvcc[offset++] = arrays.length;

    for (const array of arrays) {
        // array_completeness(1) | reserved(1) | NAL_unit_type(6)
        hvcc[offset++] = 0x80 | (array.type & 0x3F);

        // numNalus (16)
        hvcc[offset++] = (array.nalUnits.length >> 8) & 0xFF;
        hvcc[offset++] = array.nalUnits.length & 0xFF;

        for (const nalUnit of array.nalUnits) {
            hvcc[offset++] = (nalUnit.length >> 8) & 0xFF;
            hvcc[offset++] = nalUnit.length & 0xFF;

            hvcc.set(nalUnit, offset);
            offset += nalUnit.length;
        }
    }

    return hvcc;
}

export function extractHVCC(chunk: EncodedVideoChunk): Uint8Array | null {
    try {
        const parameterSets = parseHEVCNALUs(chunk);
        if (parameterSets.sps.length === 0) {
            warnLog?.log('No SPS found in chunk, cannot build HVCC');
            return null;
        }
        const hvcc = buildHVCC(parameterSets);
        infoLog?.log('Built HVCC:', { size: hvcc.length, vps: parameterSets.vps.length, sps: parameterSets.sps.length, pps: parameterSets.pps.length });
        return hvcc;
    } catch (error) {
        errorLog?.log('Error extracting HVCC:', error);
        return null;
    }
}