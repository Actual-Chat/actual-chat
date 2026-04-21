// Verifies that the msgpack-map-patch encodes BigInt values as int64 (0xd3)
// rather than letting them fall through to float64. Needed for .NET `long`
// fields like `Moment.EpochOffsetTicks` whose 2026-era values exceed
// Number.MAX_SAFE_INTEGER and must survive the wire as exact int64.

import { describe, it, expect } from 'vitest';
import { Encoder, Decoder } from '@msgpack/msgpack';
// Side-effect import: patches Encoder.prototype at module load.
import '../../../src/nodejs/src/actuallab-rpc/msgpack-map-patch';

describe('msgpack BigInt patch', () => {
    it('encodes a moderate BigInt as int64 (0xd3)', () => {
        const enc = new Encoder();
        const bytes = enc.encode(42n);
        expect(bytes[0]).toBe(0xd3);
        // Value: 0x0000000000 00002A in big-endian int64
        const dv = new DataView(bytes.buffer, bytes.byteOffset + 1, 8);
        expect(dv.getBigInt64(0, false)).toBe(42n);
    });

    it('encodes a value past Number.MAX_SAFE_INTEGER losslessly', () => {
        const enc = new Encoder();
        const v = 17767650673661280n; // ~1.8e16, past 2^53
        const bytes = enc.encode(v);
        expect(bytes[0]).toBe(0xd3);
        const dv = new DataView(bytes.buffer, bytes.byteOffset + 1, 8);
        expect(dv.getBigInt64(0, false)).toBe(v);
    });

    it('encodes negative BigInt as int64', () => {
        const enc = new Encoder();
        const v = -1234567890123456789n;
        const bytes = enc.encode(v);
        expect(bytes[0]).toBe(0xd3);
        const dv = new DataView(bytes.buffer, bytes.byteOffset + 1, 8);
        expect(dv.getBigInt64(0, false)).toBe(v);
    });

    it('encodes a value past int64 max as uint64 (0xcf)', () => {
        const enc = new Encoder();
        const v = 0xffffffffffffffffn; // max uint64
        const bytes = enc.encode(v);
        expect(bytes[0]).toBe(0xcf);
        const dv = new DataView(bytes.buffer, bytes.byteOffset + 1, 8);
        expect(dv.getBigUint64(0, false)).toBe(v);
    });

    it('throws for BigInt outside int64/uint64 range', () => {
        const enc = new Encoder();
        expect(() => enc.encode(0x1_ffffffff_ffffffffn)).toThrow(/out of msgpack/);
        expect(() => enc.encode(-0x8000000000000001n)).toThrow(/out of msgpack/);
    });

    it('encodes nested BigInt inside an object with the expected bytes', () => {
        // The .NET server reads the Moment field via an int64 formatter; our
        // only job on the wire is to ensure the bytes are 0xd3 + 8 big-endian
        // bytes. @msgpack/msgpack 2.8 doesn't preserve int64 precision on
        // decode (returns `number`), but the wire layout is what matters.
        const enc = new Encoder();
        const obj = { StartAt: 17767650673661280n };
        const bytes = enc.encode(obj);
        // Find the 0xd3 byte and verify the 8 bytes after match big-endian int64.
        const idx = Array.from(bytes).findIndex(b => b === 0xd3);
        expect(idx).toBeGreaterThanOrEqual(0);
        const dv = new DataView(bytes.buffer, bytes.byteOffset + idx + 1, 8);
        expect(dv.getBigInt64(0, false)).toBe(17767650673661280n);
    });
    // Reference to Decoder to avoid lint complaints about unused import.
    void Decoder;
});
