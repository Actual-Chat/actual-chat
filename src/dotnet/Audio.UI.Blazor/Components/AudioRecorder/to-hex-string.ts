export function toHexString (byteArray: Uint8Array) {
    const chars = new Uint8Array(byteArray.length * 2);
    const alpha = 'A'.charCodeAt(0) - 10;
    const digit = '0'.charCodeAt(0);

    let p = 0;
    for (let i = 0; i < byteArray.length; i++) {
        let nibble = byteArray[i] >>> 4;
        chars[p++] = nibble > 9 ? nibble + alpha : nibble + digit;
        nibble = byteArray[i] & 0xF;
        chars[p++] = nibble > 9 ? nibble + alpha : nibble + digit;
    }

    return String.fromCharCode.apply(null, chars);
}
