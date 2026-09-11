export function readAscii(bytes: Uint8Array, offset: number, length: number): string {
    if (offset < 0 || offset + length > bytes.length)
        return '';

    let result = '';
    for (let i = offset; i < offset + length; i++)
        result += String.fromCharCode(bytes[i]);
    return result;
}

export function readUint16BE(bytes: Uint8Array, offset: number): number {
    return (bytes[offset] << 8) | bytes[offset + 1];
}

export function readUint16LE(bytes: Uint8Array, offset: number): number {
    return bytes[offset] | (bytes[offset + 1] << 8);
}

export function readUint32BE(bytes: Uint8Array, offset: number): number {
    return ((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]) >>> 0;
}

export function readUint32LE(bytes: Uint8Array, offset: number): number {
    return (bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)) >>> 0;
}

export function writeUint32LE(bytes: Uint8Array, offset: number, value: number): void {
    bytes[offset] = value & 0xFF;
    bytes[offset + 1] = (value >>> 8) & 0xFF;
    bytes[offset + 2] = (value >>> 16) & 0xFF;
    bytes[offset + 3] = (value >>> 24) & 0xFF;
}

export function startsWith(bytes: Uint8Array, offset: number, prefix: ArrayLike<number>): boolean {
    if (offset + prefix.length > bytes.length)
        return false;

    for (let i = 0; i < prefix.length; i++) {
        if (bytes[offset + i] !== prefix[i])
            return false;
    }

    return true;
}

export function indexOfAscii(bytes: Uint8Array, text: string): number {
    const prefix = Array.from(text, c => c.charCodeAt(0));
    for (let i = 0; i + prefix.length <= bytes.length; i++) {
        if (startsWith(bytes, i, prefix))
            return i;
    }

    return -1;
}

export function concatBytes(parts: Uint8Array[]): Uint8Array {
    const result = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
    let offset = 0;
    for (const part of parts) {
        result.set(part, offset);
        offset += part.length;
    }
    return result;
}
