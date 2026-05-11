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

// Numeric values are kept stable on the wire: append new stages, never
// renumber existing ones. byte-sized so it round-trips through MessagePack
// `byte[]` cheaply on the .NET side.
export const enum FrameDropStage {
    None = 0,
    // Unspecified upstream — used when we know a drop happened earlier but
    // can't pin it to a specific operator (e.g. raw MSTP source loss).
    SenderSource = 1,
    SenderFloodGate = 2,
    SenderStampCaptureTime = 3,
    SenderAttachSourceDims = 4,
    SenderDownscale = 5,
    SenderApplyKeyframePolicy = 6,
    SenderEncode = 7,
    SenderWireSend = 8,
    SenderPushPullBuffer = 9,
    SenderRpcStream = 10,

    ServerPushStream = 20,
    ServerProcessFrames = 21,
    ServerMemoizer = 22,
    ServerSkipWhile = 23,
    ServerReceiveQualityFilter = 24,
    ServerRpcStream = 25,

    ReceiverPull = 40,
    ReceiverEpochReset = 41,
    ReceiverEncodedBuffer = 42,
    ReceiverDecode = 43,
    ReceiverPresent = 44,
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
