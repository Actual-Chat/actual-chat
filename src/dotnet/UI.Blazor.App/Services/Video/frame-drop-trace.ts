// End-to-end frame-drop attribution.
//
// Every envelope (CapturedFrame, CapturedBundle, EncodedFrame, EncodedBundle,
// ArrivedChunk, DecodedFrame) and the wire DTO carries a `dropTrace` array.
// Each entry identifies a single dropped predecessor frame, tagged with the
// stage that dropped it. The first frame of any pipeline run carries an empty
// array; subsequent frames append entries when the local detector observes a
// gap larger than the trace already covers.
//
// Wiring rule: place `traceDrops(prevStage)` BEFORE the operator whose drops
// you want to attribute to `prevStage`. The detector witnesses the gap and
// blames it on the operator immediately upstream of itself.

import { from, type PipeOperator } from 'ix-ext';

// Byte-sized so it round-trips through MessagePack `byte[]` cheaply on the
// .NET side. Ranges: 1-30 sender, 31-60 server, 61-90 receiver.
export const enum FrameDropStage {
    None = 0,

    SenderSource = 1,
    SenderFloodGate = 2,
    SenderDownscale = 3,
    SenderEncode = 4,

    ServerPushStream = 31,
    ServerMemoizer = 32,
    ServerSkipWhile = 33,
    ServerReceiveQualityFilter = 34,

    ReceiverPull = 61,
    ReceiverEncodedBuffer = 62,
    ReceiverDecode = 63,
}

// `traceDrops` is a generic AsyncIterable wrapper. Items must expose `index`
// (monotonically increasing, gaps == drops) and a mutable `dropTrace` array.
// On each item: gap = item.index - lastIndex - 1; if `gap > dropTrace.length`,
// append `(gap - dropTrace.length)` entries tagged `prevStage` — those are the
// frames the previous operator dropped without anyone tagging them.
export interface DropTraced {
    readonly index: number;
    readonly dropTrace: FrameDropStage[];
}

export function traceDrops<T extends DropTraced>(prevStage: FrameDropStage): PipeOperator<T, T> {
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<T>): AsyncIterable<T> {
        let lastIndex: number | null = null;
        for await (const item of source) {
            if (lastIndex !== null) {
                const gap = item.index - lastIndex - 1;
                if (gap > 0) {
                    const have = item.dropTrace.length;
                    const missing = gap - have;
                    for (let i = 0; i < missing; i++)
                        item.dropTrace.push(prevStage);
                }
            }
            lastIndex = item.index;
            yield item;
        }
    }
}
