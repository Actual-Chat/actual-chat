import { describe, it, expect } from 'vitest';
import {
    ActualOpusStreamParser,
} from '../../../src/nodejs/src/audio/actual-opus-stream-parser';

const PREFIX = [0x41, 0x5F, 0x4F, 0x50, 0x55, 0x53, 0x5F, 0x53];

function buildHeader(preSkip: number, createdAtTicks: bigint): Uint8Array {
    const buf = new Uint8Array(PREFIX.length + 1 + 2 + 8);
    buf.set(PREFIX, 0);
    buf[PREFIX.length] = 3; // version
    const view = new DataView(buf.buffer);
    view.setInt16(PREFIX.length + 1, preSkip, false);
    view.setBigInt64(PREFIX.length + 3, createdAtTicks, false);
    return buf;
}

function buildPacket(size: number, fill: number): Uint8Array {
    const body = new Uint8Array(2 + size);
    body[0] = (size >> 8) & 0xff;
    body[1] = size & 0xff;
    for (let i = 0; i < size; i++) body[2 + i] = fill;
    return body;
}

function concat(...parts: Uint8Array[]): Uint8Array {
    const total = parts.reduce((n, p) => n + p.length, 0);
    const out = new Uint8Array(total);
    let off = 0;
    for (const p of parts) { out.set(p, off); off += p.length; }
    return out;
}

describe('ActualOpusStreamParser', () => {
    it('parses header on first chunk', () => {
        const parser = new ActualOpusStreamParser();
        const header = buildHeader(312, 1700000000000000n);
        const pkts = parser.parse(header);
        expect(pkts).toEqual([]);
        expect(parser.header).not.toBeNull();
        expect(parser.header!.version).toBe(3);
        expect(parser.header!.preSkip).toBe(312);
        expect(parser.header!.createdAtTicks).toBe(1700000000000000n);
    });

    it('parses a single packet following the header', () => {
        const parser = new ActualOpusStreamParser();
        const buf = concat(buildHeader(0, 0n), buildPacket(5, 0xab));
        const pkts = parser.parse(buf);
        expect(pkts).toHaveLength(1);
        expect(pkts[0].data.length).toBe(5);
        expect(pkts[0].offsetMs).toBe(20);
        for (const b of pkts[0].data) expect(b).toBe(0xab);
    });

    it('parses multiple packets and increments offset', () => {
        const parser = new ActualOpusStreamParser();
        const buf = concat(
            buildHeader(0, 0n),
            buildPacket(3, 0x11),
            buildPacket(7, 0x22),
            buildPacket(4, 0x33),
        );
        const pkts = parser.parse(buf);
        expect(pkts).toHaveLength(3);
        expect(pkts.map(p => p.offsetMs)).toEqual([20, 40, 60]);
        expect(pkts[0].data.length).toBe(3);
        expect(pkts[1].data.length).toBe(7);
        expect(pkts[2].data.length).toBe(4);
    });

    it('handles header split across two chunks', () => {
        const parser = new ActualOpusStreamParser();
        const header = buildHeader(160, 42n);
        const pkts1 = parser.parse(header.subarray(0, 10));
        expect(pkts1).toEqual([]);
        expect(parser.header).toBeNull();
        const pkts2 = parser.parse(header.subarray(10));
        expect(pkts2).toEqual([]);
        expect(parser.header!.preSkip).toBe(160);
        expect(parser.header!.createdAtTicks).toBe(42n);
    });

    it('handles packet split across chunks', () => {
        const parser = new ActualOpusStreamParser();
        const full = concat(buildHeader(0, 0n), buildPacket(6, 0xcc));
        // Split mid-packet: after header (19) + 2 size bytes + 3 of 6 body bytes = 24.
        const pkts1 = parser.parse(full.subarray(0, 24));
        expect(pkts1).toEqual([]);
        const pkts2 = parser.parse(full.subarray(24));
        expect(pkts2).toHaveLength(1);
        expect(pkts2[0].data.length).toBe(6);
        for (const b of pkts2[0].data) expect(b).toBe(0xcc);
    });

    it('returns fresh ArrayBuffer per packet (safe to transfer)', () => {
        const parser = new ActualOpusStreamParser();
        const buf = concat(buildHeader(0, 0n), buildPacket(4, 0x01), buildPacket(4, 0x02));
        const pkts = parser.parse(buf);
        expect(pkts[0].data.buffer).not.toBe(pkts[1].data.buffer);
        expect(pkts[0].data.byteOffset).toBe(0);
        expect(pkts[1].data.byteOffset).toBe(0);
    });

    it('throws on bad prefix', () => {
        const parser = new ActualOpusStreamParser();
        const bad = new Uint8Array(20);
        bad.set([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], 0);
        expect(() => parser.parse(bad)).toThrow(/invalid A_OPUS_S/);
    });

    it('throws on unsupported version', () => {
        const parser = new ActualOpusStreamParser();
        const buf = buildHeader(0, 0n);
        buf[PREFIX.length] = 2; // unsupported via this parser
        expect(() => parser.parse(buf)).toThrow(/unsupported version/);
    });

    it('reset() clears buffered state and offset', () => {
        const parser = new ActualOpusStreamParser();
        parser.parse(concat(buildHeader(0, 0n), buildPacket(4, 0x00)));
        expect(parser.header).not.toBeNull();
        parser.reset();
        expect(parser.header).toBeNull();
        // After reset, a new header is expected.
        const pkts = parser.parse(buildHeader(99, 1n));
        expect(pkts).toEqual([]);
        expect(parser.header!.preSkip).toBe(99);
    });
});
