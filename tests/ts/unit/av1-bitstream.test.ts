import { describe, expect, it } from 'vitest';
import {
    normalizeAv1FrameSize,
    readAv1FrameSizes,
} from '../../../src/dotnet/UI.Blazor.App/Services/Video/av1-bitstream';
import {
    type Av1Fixture,
    nvenc480,
    nvenc640Delta,
    nvencKeyFrames,
    softwareKeyFrames,
} from './fixtures/av1-keyframes';

function decode(fixture: Av1Fixture): Uint8Array {
    const binary = atob(fixture.base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);

    return bytes;
}

function concat(parts: Uint8Array[]): Uint8Array {
    const result = new Uint8Array(parts.reduce((sum, p) => sum + p.length, 0));
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }

    return result;
}

// Minimal OBU splitter, independent of the module under test, for building
// malformed streams out of a good one. Assumes obu_has_size_field, which every
// WebCodecs encoder sets.
function splitObus(data: Uint8Array): { type: number; bytes: Uint8Array }[] {
    const obus: { type: number; bytes: Uint8Array }[] = [];
    let offset = 0;
    while (offset < data.length) {
        const type = (data[offset] >> 3) & 0xF;
        let cursor = offset + 1 + ((data[offset] >> 2) & 1);
        let size = 0;
        for (let shift = 0; ; shift += 7) {
            const byte = data[cursor++];
            size += (byte & 0x7F) * 2 ** shift;
            if ((byte & 0x80) === 0)
                break;
        }

        obus.push({ type, bytes: data.subarray(offset, cursor + size) });
        offset = cursor + size;
    }

    return obus;
}

describe('readAv1FrameSizes', () => {
    it.each(nvencKeyFrames)(
        'reports the oversized max_frame_size NVENC writes at $width x $height',
        fixture => {
            const sizes = readAv1FrameSizes(decode(fixture));

            expect(sizes).not.toBeNull();
            expect(sizes!.codedWidth).toBe(fixture.width);
            expect(sizes!.codedHeight).toBe(fixture.height);
            expect(sizes!.maxWidth).toBe(1920);
            expect(sizes!.maxHeight).toBe(1088);
            expect(sizes!.renderWidth).toBe(1920);
            expect(sizes!.renderHeight).toBe(1088);
        });

    it.each(softwareKeyFrames)(
        'reports matching sizes for $encoder at $width x $height',
        fixture => {
            const sizes = readAv1FrameSizes(decode(fixture));

            expect(sizes).not.toBeNull();
            expect(sizes!.codedWidth).toBe(fixture.width);
            expect(sizes!.codedHeight).toBe(fixture.height);
            expect(sizes!.maxWidth).toBe(fixture.width);
            expect(sizes!.maxHeight).toBe(fixture.height);
            expect(sizes!.renderWidth).toBe(fixture.width);
            expect(sizes!.renderHeight).toBe(fixture.height);
        });

    it('returns null for a delta frame, which carries no sequence header', () => {
        expect(readAv1FrameSizes(decode(nvenc640Delta))).toBeNull();
    });
});

describe('normalizeAv1FrameSize', () => {
    it.each(nvencKeyFrames)(
        'rewrites both size fields to $width x $height without resizing the chunk',
        fixture => {
            const data = decode(fixture);
            const originalLength = data.length;

            expect(normalizeAv1FrameSize(data, fixture.width, fixture.height)).toBe(true);

            expect(data.length).toBe(originalLength);
            const sizes = readAv1FrameSizes(data)!;
            expect(sizes.maxWidth).toBe(fixture.width);
            expect(sizes.maxHeight).toBe(fixture.height);
            expect(sizes.codedWidth).toBe(fixture.width);
            expect(sizes.codedHeight).toBe(fixture.height);
            expect(sizes.renderWidth).toBe(fixture.width);
            expect(sizes.renderHeight).toBe(fixture.height);
        });

    it.each(nvencKeyFrames)('is idempotent at $width x $height', fixture => {
        const data = decode(fixture);
        normalizeAv1FrameSize(data, fixture.width, fixture.height);
        const afterFirst = Uint8Array.from(data);

        expect(normalizeAv1FrameSize(data, fixture.width, fixture.height)).toBe(false);
        expect(data).toEqual(afterFirst);
    });

    it.each(softwareKeyFrames)(
        'leaves $encoder output at $width x $height byte-identical',
        fixture => {
            const data = decode(fixture);
            const original = Uint8Array.from(data);

            expect(normalizeAv1FrameSize(data, fixture.width, fixture.height)).toBe(false);
            expect(data).toEqual(original);
        });

    it.each(nvencKeyFrames)(
        'refuses dims that disagree with the coded size at $width x $height',
        fixture => {
            const data = decode(fixture);
            const original = Uint8Array.from(data);

            expect(normalizeAv1FrameSize(data, fixture.width + 2, fixture.height)).toBe(false);
            expect(normalizeAv1FrameSize(data, fixture.width, fixture.height + 2)).toBe(false);
            expect(data).toEqual(original);
        });

    it('refuses non-positive dims', () => {
        const fixture = nvencKeyFrames[0];
        const data = decode(fixture);
        const original = Uint8Array.from(data);

        expect(normalizeAv1FrameSize(data, 0, fixture.height)).toBe(false);
        expect(normalizeAv1FrameSize(data, fixture.width, -1)).toBe(false);
        expect(data).toEqual(original);
    });

    it('refuses a delta frame', () => {
        const data = decode(nvenc640Delta);
        const original = Uint8Array.from(data);

        expect(normalizeAv1FrameSize(data, nvenc640Delta.width, nvenc640Delta.height)).toBe(false);
        expect(data).toEqual(original);
    });

    it('refuses truncated and corrupt input without throwing', () => {
        const fixture = nvencKeyFrames[0];
        const full = decode(fixture);
        for (const length of [0, 1, 2, 5, 12, full.length - 1]) {
            const truncated = Uint8Array.from(full.subarray(0, length));
            const original = Uint8Array.from(truncated);

            expect(normalizeAv1FrameSize(truncated, fixture.width, fixture.height)).toBe(false);
            expect(truncated).toEqual(original);
        }

        const corrupt = Uint8Array.from(full);
        corrupt[0] = 0xFF; // obu_forbidden_bit set
        expect(normalizeAv1FrameSize(corrupt, fixture.width, fixture.height)).toBe(false);
        expect(normalizeAv1FrameSize(new Uint8Array(64), fixture.width, fixture.height)).toBe(false);
    });

    // Every other assertion here reads the result back through this module's
    // own parser, so a matching drift in parser and writer would be invisible.
    // These bytes were produced by a separate implementation of the spec walk
    // and confirmed to decode at the right size in Firefox.
    it('produces the expected bytes for a known NVENC key frame', () => {
        const golden =
            'EgAKDAAAAEao75D+ABDMAjJlEAAIA75D4DvgIegkCAggggEABADBABJyMw8cc2azTwWoHBJ+bF2lwd58pvEgZabrcj4nFfbO'
            + 'zPujKbaJUIkmNfYBw9t2pfES7OrEtLP8iShcq405z3GN7iSYK1+dOZSLxsivZcg=';
        const data = decode(nvenc480);

        expect(normalizeAv1FrameSize(data, nvenc480.width, nvenc480.height)).toBe(true);
        expect(data).toEqual(decode({ ...nvenc480, base64: golden }));
    });

    it('refuses a chunk carrying a second sequence header', () => {
        const fixture = nvencKeyFrames[0];
        const full = decode(fixture);
        const obus = splitObus(full);
        const sequence = obus.find(obu => obu.type === 1)!;

        // Rebuilding the fixture from the split must reproduce it exactly,
        // otherwise the duplicate below would be refused for being malformed
        // rather than for carrying two sequence headers.
        expect(concat(obus.map(obu => obu.bytes))).toEqual(full);

        const duplicated = concat([...obus.map(obu => obu.bytes), sequence.bytes]);
        const original = Uint8Array.from(duplicated);
        expect(splitObus(duplicated).filter(obu => obu.type === 1)).toHaveLength(2);

        expect(readAv1FrameSizes(duplicated)).toBeNull();
        expect(normalizeAv1FrameSize(duplicated, fixture.width, fixture.height)).toBe(false);
        expect(duplicated).toEqual(original);
    });

    it('touches only the two size fields', () => {
        const fixture = nvencKeyFrames[0];
        const data = decode(fixture);
        const original = Uint8Array.from(data);

        expect(normalizeAv1FrameSize(data, fixture.width, fixture.height)).toBe(true);

        const changed: number[] = [];
        for (let i = 0; i < data.length; i++)
            if (data[i] !== original[i])
                changed.push(i);
        expect(changed.length).toBeGreaterThan(0);

        // max_frame_size spans at most 3 bytes in the sequence header OBU and
        // render_size at most 5 in the frame OBU, both of which sit in the
        // first ~40 bytes; the tile data after them must be untouched.
        expect(changed.length).toBeLessThanOrEqual(8);
        expect(Math.max(...changed)).toBeLessThan(48);
    });
});
