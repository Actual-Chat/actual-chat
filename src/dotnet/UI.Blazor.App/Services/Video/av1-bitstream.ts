// Chrome's hardware AV1 encoder (NVENC) writes the encoder session's maximum
// frame size — 1920x1088 — into `max_frame_width/height` in the sequence header
// and into `render_width/height` in the frame header, whatever resolution it was
// configured for. Chromium's own decoder ignores both and uses the per-frame
// coded size, so it looks right there; Gecko takes `max_frame_size` as the
// frame's size and reports a 480x272 picture as a 1920x1088 one with the image
// in the top-left corner. Both fields have a bit width fixed by the sequence
// header (`frame_width_bits_minus_1`) and by the spec (16 bits for render size),
// so correcting them is an in-place rewrite: no bit after them moves and the OBU
// size is unchanged.
//
// Syntax is AV1 1.0.0 §5.5 (sequence header) and §5.9 (uncompressed frame
// header). Every construct we have not seen a real encoder emit is a bail-out
// rather than an untested code path — a misparsed offset would corrupt the
// stream for every viewer, and "did not patch" only costs Gecko the rendering
// it already gets wrong today.

const OBU_SEQUENCE_HEADER = 1;
const OBU_FRAME_HEADER = 3;
const OBU_FRAME = 6;
const OBU_REDUNDANT_FRAME_HEADER = 7;

const KEY_FRAME = 0;
const SELECT_SCREEN_CONTENT_TOOLS = 2;
const SELECT_INTEGER_MV = 2;
const RENDER_SIZE_BITS = 16;
const MAX_LEB128_BYTES = 8;

export interface Av1FrameSizes {
    maxWidth: number;
    maxHeight: number;
    codedWidth: number;
    codedHeight: number;
    renderWidth: number;
    renderHeight: number;
}

export function readAv1FrameSizes(data: Uint8Array): Av1FrameSizes | null {
    const located = locateFrameSizes(data);
    if (located === null)
        return null;

    const { sequence, frame } = located;

    return {
        maxWidth: sequence.maxWidth,
        maxHeight: sequence.maxHeight,
        codedWidth: frame.codedWidth,
        codedHeight: frame.codedHeight,
        renderWidth: frame.renderWidth,
        renderHeight: frame.renderHeight,
    };
}

// Rewrites `max_frame_size` and `render_size` to (width, height) in place when
// the encoder declared something larger; `data` is left untouched and `false`
// returned when the chunk is already correct or anything about it is
// unrecognized. Both fields are written together or not at all — patching only
// one would leave render_size larger than max_frame_size.
export function normalizeAv1FrameSize(data: Uint8Array, width: number, height: number): boolean {
    if (width <= 0 || height <= 0)
        return false;

    const located = locateFrameSizes(data);
    if (located === null)
        return false;

    const { sequence, frame } = located;
    // The caller's dims come from the encoder config, so a walk that drifted
    // would have to land on exactly those two values to get past this.
    if (frame.codedWidth !== width || frame.codedHeight !== height)
        return false;
    if (sequence.maxWidth < width || sequence.maxHeight < height)
        return false;
    if (width > 1 << sequence.widthBits || height > 1 << sequence.heightBits)
        return false;

    const patchMax = sequence.maxWidth !== width || sequence.maxHeight !== height;
    const patchRender = frame.renderSizeCoded
        && (frame.renderWidth !== width || frame.renderHeight !== height);
    if (!patchMax && !patchRender)
        return false;

    if (patchMax) {
        writeBits(sequence.payload, sequence.maxWidthBitOffset, sequence.widthBits, width - 1);
        writeBits(sequence.payload, sequence.maxHeightBitOffset, sequence.heightBits, height - 1);
    }
    if (patchRender) {
        writeBits(frame.payload, frame.renderWidthBitOffset, RENDER_SIZE_BITS, width - 1);
        writeBits(frame.payload, frame.renderWidthBitOffset + RENDER_SIZE_BITS, RENDER_SIZE_BITS, height - 1);
    }

    return true;
}

// Private methods

interface Obu {
    type: number;
    payload: Uint8Array;
}

interface SequenceHeader {
    payload: Uint8Array;
    maxWidth: number;
    maxHeight: number;
    maxWidthBitOffset: number;
    maxHeightBitOffset: number;
    widthBits: number;
    heightBits: number;
    reducedStillPicture: boolean;
    frameIdNumbersPresent: boolean;
    idLength: number;
    forceScreenContentTools: number;
    forceIntegerMv: number;
    orderHintBits: number;
    enableSuperres: boolean;
}

interface FrameSizes {
    payload: Uint8Array;
    codedWidth: number;
    codedHeight: number;
    renderWidth: number;
    renderHeight: number;
    renderSizeCoded: boolean;
    renderWidthBitOffset: number;
}

class BitstreamError extends Error {}

class BitReader {
    private readonly bytes: Uint8Array;
    private position = 0;

    constructor(bytes: Uint8Array) {
        this.bytes = bytes;
    }

    get bitPosition(): number {
        return this.position;
    }

    f(count: number): number {
        let value = 0;
        for (let i = 0; i < count; i++) {
            if (this.position >= this.bytes.length * 8)
                throw new BitstreamError('read past end of OBU');

            value = value * 2 + ((this.bytes[this.position >> 3] >> (7 - (this.position & 7))) & 1);
            this.position++;
        }

        return value;
    }

    uvlc(): number {
        let leadingZeros = 0;
        while (this.f(1) === 0)
            leadingZeros++;
        if (leadingZeros >= 32)
            throw new BitstreamError('uvlc out of range');

        return this.f(leadingZeros) + 2 ** leadingZeros - 1;
    }
}

function writeBits(bytes: Uint8Array, bitPosition: number, count: number, value: number): void {
    for (let i = 0; i < count; i++) {
        const position = bitPosition + i;
        const mask = 1 << (7 - (position & 7));
        if ((value >>> (count - 1 - i)) & 1)
            bytes[position >> 3] |= mask;
        else
            bytes[position >> 3] &= ~mask;
    }
}

function readLeb128(data: Uint8Array, offset: number): { value: number; length: number } | null {
    let value = 0;
    for (let i = 0; i < MAX_LEB128_BYTES; i++) {
        if (offset + i >= data.length)
            return null;

        const byte = data[offset + i];
        value += (byte & 0x7F) * 2 ** (7 * i);
        if ((byte & 0x80) === 0)
            return { value, length: i + 1 };
    }

    return null;
}

function readObus(data: Uint8Array): Obu[] | null {
    const obus: Obu[] = [];
    let offset = 0;
    while (offset < data.length) {
        const header = data[offset];
        if ((header & 0x80) !== 0)
            return null;

        const type = (header >> 3) & 0xF;
        const hasExtension = (header >> 2) & 1;
        const hasSize = (header >> 1) & 1;
        let payloadOffset = offset + 1 + hasExtension;
        let size: number;
        if (hasSize) {
            const length = readLeb128(data, payloadOffset);
            if (length === null)
                return null;

            size = length.value;
            payloadOffset += length.length;
        }
        else
            size = data.length - payloadOffset;
        if (payloadOffset + size > data.length)
            return null;

        obus.push({ type, payload: data.subarray(payloadOffset, payloadOffset + size) });
        offset = payloadOffset + size;
    }

    return obus;
}

function parseSequenceHeader(payload: Uint8Array): SequenceHeader | null {
    const reader = new BitReader(payload);
    reader.f(3); // seq_profile
    reader.f(1); // still_picture
    const reducedStillPicture = reader.f(1) === 1;
    if (reducedStillPicture)
        reader.f(5); // seq_level_idx[0]
    else {
        if (reader.f(1) === 1) { // timing_info_present_flag
            reader.f(32); // num_units_in_display_tick
            reader.f(32); // time_scale
            if (reader.f(1) === 1) // equal_picture_interval
                reader.uvlc();
            if (reader.f(1) === 1) // decoder_model_info_present_flag
                return null;
        }
        const initialDisplayDelayPresent = reader.f(1) === 1;
        const operatingPointCount = reader.f(5) + 1;
        for (let i = 0; i < operatingPointCount; i++) {
            reader.f(12); // operating_point_idc
            if (reader.f(5) > 7) // seq_level_idx
                reader.f(1); // seq_tier
            if (initialDisplayDelayPresent && reader.f(1) === 1)
                reader.f(4); // initial_display_delay_minus_1
        }
    }

    const widthBits = reader.f(4) + 1;
    const heightBits = reader.f(4) + 1;
    const maxWidthBitOffset = reader.bitPosition;
    const maxWidth = reader.f(widthBits) + 1;
    const maxHeightBitOffset = reader.bitPosition;
    const maxHeight = reader.f(heightBits) + 1;
    const frameIdNumbersPresent = !reducedStillPicture && reader.f(1) === 1;
    let idLength = 0;
    if (frameIdNumbersPresent) {
        const deltaFrameIdLength = reader.f(4) + 2;
        idLength = reader.f(3) + 1 + deltaFrameIdLength;
    }
    reader.f(3); // use_128x128_superblock, enable_filter_intra, enable_intra_edge_filter

    let forceScreenContentTools = SELECT_SCREEN_CONTENT_TOOLS;
    let forceIntegerMv = SELECT_INTEGER_MV;
    let orderHintBits = 0;
    if (!reducedStillPicture) {
        reader.f(4); // enable_interintra_compound .. enable_dual_filter
        const enableOrderHint = reader.f(1) === 1;
        if (enableOrderHint)
            reader.f(2); // enable_jnt_comp, enable_ref_frame_mvs
        forceScreenContentTools = reader.f(1) === 1 // seq_choose_screen_content_tools
            ? SELECT_SCREEN_CONTENT_TOOLS
            : reader.f(1);
        if (forceScreenContentTools > 0) {
            forceIntegerMv = reader.f(1) === 1 // seq_choose_integer_mv
                ? SELECT_INTEGER_MV
                : reader.f(1);
        }
        if (enableOrderHint)
            orderHintBits = reader.f(3) + 1;
    }
    const enableSuperres = reader.f(1) === 1;

    return {
        payload, maxWidth, maxHeight, maxWidthBitOffset, maxHeightBitOffset,
        widthBits, heightBits, reducedStillPicture, frameIdNumbersPresent, idLength,
        forceScreenContentTools, forceIntegerMv, orderHintBits, enableSuperres,
    };
}

function parseFrameSizes(payload: Uint8Array, sequence: SequenceHeader): FrameSizes | null {
    const reader = new BitReader(payload);
    if (!sequence.reducedStillPicture) {
        if (reader.f(1) === 1) // show_existing_frame
            return null;
        // Only a shown key frame is walked: it is the one that carries a
        // sequence header, and its error_resilient_mode, primary_ref_frame and
        // refresh_frame_flags are all inferred rather than coded, which keeps
        // the walk to frame_size() short enough to be worth trusting.
        if (reader.f(2) !== KEY_FRAME || reader.f(1) !== 1) // frame_type, show_frame
            return null;
    }

    reader.f(1); // disable_cdf_update
    const allowScreenContentTools = sequence.forceScreenContentTools === SELECT_SCREEN_CONTENT_TOOLS
        ? reader.f(1)
        : sequence.forceScreenContentTools;
    if (allowScreenContentTools !== 0 && sequence.forceIntegerMv === SELECT_INTEGER_MV)
        reader.f(1); // force_integer_mv
    if (sequence.frameIdNumbersPresent)
        reader.f(sequence.idLength); // current_frame_id

    const frameSizeOverride = sequence.reducedStillPicture ? 0 : reader.f(1);
    reader.f(sequence.orderHintBits); // order_hint

    let codedWidth = sequence.maxWidth;
    let codedHeight = sequence.maxHeight;
    if (frameSizeOverride === 1) {
        codedWidth = reader.f(sequence.widthBits) + 1;
        codedHeight = reader.f(sequence.heightBits) + 1;
    }
    // Superres rescales the frame after frame_size(), so the caller's configured
    // dims would no longer be the coded ones and the cross-check below breaks.
    if (sequence.enableSuperres && reader.f(1) === 1)
        return null;

    const renderSizeCoded = reader.f(1) === 1; // render_and_frame_size_different
    const renderWidthBitOffset = reader.bitPosition;
    const renderWidth = renderSizeCoded ? reader.f(RENDER_SIZE_BITS) + 1 : codedWidth;
    const renderHeight = renderSizeCoded ? reader.f(RENDER_SIZE_BITS) + 1 : codedHeight;

    return {
        payload, codedWidth, codedHeight, renderWidth, renderHeight,
        renderSizeCoded, renderWidthBitOffset,
    };
}

function locateFrameSizes(data: Uint8Array): { sequence: SequenceHeader; frame: FrameSizes } | null {
    const obus = readObus(data);
    if (obus === null)
        return null;

    // A repeated sequence header, or a redundant frame header mirroring the one
    // we patch, would leave the copies disagreeing — nonconformant, and a
    // decoder that reads the last copy would see the unpatched size.
    const sequenceObus = obus.filter(obu => obu.type === OBU_SEQUENCE_HEADER);
    const frameObus = obus.filter(obu => obu.type === OBU_FRAME || obu.type === OBU_FRAME_HEADER);
    if (sequenceObus.length !== 1 || frameObus.length !== 1)
        return null;
    if (obus.some(obu => obu.type === OBU_REDUNDANT_FRAME_HEADER))
        return null;

    try {
        const sequence = parseSequenceHeader(sequenceObus[0].payload);
        if (sequence === null)
            return null;

        const frame = parseFrameSizes(frameObus[0].payload, sequence);
        if (frame === null)
            return null;

        return { sequence, frame };
    }
    catch {
        return null;
    }
}
