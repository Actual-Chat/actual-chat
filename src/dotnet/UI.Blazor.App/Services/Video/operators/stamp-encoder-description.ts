// Caches per-layer decoder description bytes (avcC / HVCC / AV1C) emitted
// by the WebCodecs encoder on the first chunk after configure(), and
// stamps every subsequent EncodedFrame with the cached value. Lives
// upstream of wireGate so the description survives gate drops during
// JoinVideoCallModal warmup — otherwise the receiver gets no avcC and
// the AVCC H.264 decoder fails with "key frame required after configure()".

import { from, type PipeOperator } from 'ix-ext';
import type { EncodedBundle } from '../frame-envelopes';

export function stampEncoderDescription(): PipeOperator<EncodedBundle, EncodedBundle> {
    return source => {
        return from(impl());

        async function* impl(): AsyncIterable<EncodedBundle> {
            const cache = new Map<number, Uint8Array>();
            for await (const bundle of source) {
                for (const frame of bundle.layers) {
                    const raw = frame.metadata.decoderConfig?.description as
                        ArrayBuffer | ArrayBufferView | undefined;
                    if (raw) {
                        const bytes = toUint8Array(raw);
                        cache.set(frame.layerId, bytes);
                        frame.description = bytes;
                    } else {
                        const cached = cache.get(frame.layerId);
                        if (cached)
                            frame.description = cached;
                    }
                }
                yield bundle;
            }
        }
    };
}

function toUint8Array(source: ArrayBuffer | ArrayBufferView): Uint8Array {
    if (source instanceof Uint8Array) return new Uint8Array(source);
    if (ArrayBuffer.isView(source)) {
        const view = new Uint8Array(
            source.buffer as ArrayBuffer, source.byteOffset, source.byteLength);
        return new Uint8Array(view);
    }
    return new Uint8Array(source.slice(0));
}
