import type { VideoFrameDto } from './streaming-api';

export type VideoCodecCategory = 'h264' | 'hevc' | 'vp9' | 'av1';

export function getVideoCodecCategory(codec: string | null | undefined): VideoCodecCategory | null {
    const value = codec?.toLowerCase();
    if (value === 'h264' || value?.startsWith('avc1') || value?.startsWith('avc3'))
        return 'h264';
    if (value === 'hevc' || value?.startsWith('hvc1') || value?.startsWith('hev1'))
        return 'hevc';
    if (value === 'vp9' || value?.startsWith('vp09'))
        return 'vp9';
    if (value === 'av1' || value?.startsWith('av01'))
        return 'av1';

    return null;
}

export function filterSupportedVideoFrames<
    T extends Pick<VideoFrameDto, 'Codec' | 'Index' | 'KeyFrameIndex' | 'LayerId'>
>(
    source: AsyncIterable<T>,
    supportedCodecs: readonly string[],
): AsyncIterable<T> {
    const supported = new Set(supportedCodecs.map(getVideoCodecCategory).filter(x => x !== null));
    // Closing must interrupt the pull even when next never yields a compatible frame.
    return {
        [Symbol.asyncIterator](): AsyncIterator<T> {
            const iterator = source[Symbol.asyncIterator]();
            const allowedLayers = new Set<number>();
            let isClosed = false;
            const canRead = () => !isClosed;
            return {
                async next(): Promise<IteratorResult<T>> {
                    while (canRead()) {
                        const result = await iterator.next();
                        if (isClosed || result.done) {
                            isClosed = true;
                            return { done: true, value: undefined };
                        }
                        const frame = result.value;
                        const layerId = frame.LayerId ?? 0;
                        if ((frame.Index ?? 0) === (frame.KeyFrameIndex ?? 0)) {
                            const category = getVideoCodecCategory(frame.Codec);
                            if (category !== null && supported.has(category))
                                allowedLayers.add(layerId);
                            else
                                allowedLayers.delete(layerId);
                        }
                        if (allowedLayers.has(layerId))
                            return result;
                    }
                    return { done: true, value: undefined };
                },
                return(): Promise<IteratorResult<T>> {
                    isClosed = true;
                    return iterator.return?.() ?? Promise.resolve({ done: true, value: undefined });
                },
            };
        },
    };
}
