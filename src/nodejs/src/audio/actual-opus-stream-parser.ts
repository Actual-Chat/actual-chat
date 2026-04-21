// TS port of ActualChat's native Opus stream format (A_OPUS_S).
// Mirror of src/dotnet/Api/Audio/ActualOpusStreamHeader.cs and
// src/dotnet/Api/Audio/ActualOpusStreamConverter.cs (read path).
//
// Header layout (version 3, 19 bytes):
//   [0..7]   "A_OPUS_S" (8 bytes)
//   [8]      version byte (3)
//   [9..10]  preSkip, int16 big-endian
//   [11..18] createdAtTicks, int64 big-endian (Moment = 100-ns units from Unix epoch)
//
// Container wire (ActualOpusStreamConverter) additionally wraps each Opus packet
// in a uint16 big-endian length prefix. In the pull path we receive Opus packets
// as individual `LiveAudioFrame.Data` items instead — so `parseHeader` is enough
// and the streaming parser below is kept only for container-wire use-cases.

const OPUS_FRAME_DURATION_MS = 20;

// "A_OPUS_S"
const PREFIX = new Uint8Array([0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53]);
const HEADER_V3_LENGTH = PREFIX.length + 1 /* version */ + 2 /* preSkip */ + 8 /* createdAt */;

export interface ActualOpusStreamHeader {
    version: number;
    preSkip: number;
    createdAtTicks: bigint;
}

export interface ActualOpusPacket {
    /** Fresh ArrayBuffer per packet — safe to transfer to a worker. */
    data: Uint8Array;
    /** Playback offset from stream start, milliseconds. */
    offsetMs: number;
}

/** Returns true if the buffer's first bytes match the A_OPUS_S prefix. */
export function isActualOpusStreamHeader(bytes: Uint8Array): boolean {
    if (bytes.length < PREFIX.length)
        return false;
    for (let i = 0; i < PREFIX.length; i++) {
        if (bytes[i] !== PREFIX[i])
            return false;
    }
    return true;
}

/** Parse an A_OPUS_S header out of a one-shot buffer (not streaming).
 *  Use this on the pull path where live streams prepend a header as the first
 *  `LiveAudioFrame` with Offset < 0. Replay streams don't prepend — detect via
 *  `isActualOpusStreamHeader(firstChunk)` before calling this. */
export function parseActualOpusStreamHeader(bytes: Uint8Array): ActualOpusStreamHeader {
    if (bytes.length < HEADER_V3_LENGTH)
        throw new Error(`A_OPUS_S header too short: ${bytes.length} < ${HEADER_V3_LENGTH}`);
    if (!isActualOpusStreamHeader(bytes))
        throw new Error('A_OPUS_S header prefix mismatch');
    const version = bytes[PREFIX.length];
    if (version !== 3)
        throw new Error(`A_OPUS_S unsupported version ${version} (expected 3)`);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    return {
        version,
        preSkip: view.getInt16(PREFIX.length + 1, false),
        createdAtTicks: view.getBigInt64(PREFIX.length + 1 + 2, false),
    };
}

/**
 * Streaming parser for the A_OPUS_S format. Feed it raw byte chunks as they
 * arrive; each `parse(chunk)` call returns zero or more Opus packets extracted
 * so far. Partial packets and the header are buffered internally.
 *
 * The `header` field is populated once the header has been read (after the
 * first chunk that completes the 19-byte prefix).
 */
export class ActualOpusStreamParser {
    public header: ActualOpusStreamHeader | null = null;
    private pending = new Uint8Array(0);
    private offsetMs = 0;

    /** Process a byte chunk, return the Opus packets newly decoded. */
    public parse(chunk: Uint8Array): ActualOpusPacket[] {
        if (chunk.length === 0)
            return [];

        // Append to pending buffer.
        const merged = new Uint8Array(this.pending.length + chunk.length);
        merged.set(this.pending, 0);
        merged.set(chunk, this.pending.length);

        let pos = 0;
        const view = new DataView(merged.buffer, merged.byteOffset, merged.byteLength);

        if (this.header === null) {
            if (merged.length < HEADER_V3_LENGTH) {
                this.pending = merged;
                return [];
            }
            for (let i = 0; i < PREFIX.length; i++) {
                if (merged[i] !== PREFIX[i])
                    throw new Error('ActualOpusStreamParser: invalid A_OPUS_S header prefix');
            }
            const version = merged[PREFIX.length];
            if (version !== 3)
                throw new Error(`ActualOpusStreamParser: unsupported version ${version} (expected 3)`);
            const preSkip = view.getInt16(PREFIX.length + 1, false);
            const createdAtTicks = view.getBigInt64(PREFIX.length + 1 + 2, false);
            this.header = { version, preSkip, createdAtTicks };
            pos = HEADER_V3_LENGTH;
        }

        const result: ActualOpusPacket[] = [];
        while (pos + 2 <= merged.length) {
            const packetSize = view.getUint16(pos, false);
            if (pos + 2 + packetSize > merged.length)
                break;
            // slice() copies into a fresh ArrayBuffer → safe to transfer to a worker.
            const data = merged.slice(pos + 2, pos + 2 + packetSize);
            pos += 2 + packetSize;
            this.offsetMs += OPUS_FRAME_DURATION_MS;
            if (this.offsetMs >= 0)
                result.push({ data, offsetMs: this.offsetMs });
        }

        this.pending = pos >= merged.length
            ? new Uint8Array(0)
            : merged.slice(pos);
        return result;
    }

    /** Reset the parser — drop any buffered bytes and rewind the offset clock.
     *  Used after a stream reset where we expect a new header. */
    public reset(): void {
        this.header = null;
        this.pending = new Uint8Array(0);
        this.offsetMs = 0;
    }
}
