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
export enum FrameDropStage {
    None = 0,

    SenderSource = 1,
    SenderFloodGate = 2,
    SenderDownscale = 3,
    SenderEncode = 4,
    // Intentional temporal downsample: frames dropped before encode to hit a
    // demand-driven target fps (non-focused streams pace to ~10fps). Not a
    // failure — excluded from senderDropRatioEma.
    SenderFpsPacing = 5,

    ServerPushStream = 31,
    ServerMemoizer = 32,
    ServerSkipWhile = 33,
    ServerReceiveQualityFilter = 34,

    ReceiverPull = 61,
    ReceiverEncodedBuffer = 62,
    ReceiverDecode = 63,
    // Terminal sink — drops happen directly inside the present operator
    // (skip-mode catch-up, write failure). The aggregator on this stage
    // also walks every surviving frame's dropTrace into the per-stream
    // histogram, so this is the canonical end-of-pipe count point.
    ReceiverPresent = 64,
    // Intentional pre-decode skip-to-live: when the encoded buffer holds more
    // than the skip-to-live span, chunks before the newest buffered keyframe
    // are dropped to reach the live edge. Not a failure / not a kill stage.
    ReceiverSkipToLive = 65,
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

const RecordingKillStages = new Set<number>([
    FrameDropStage.SenderSource,
    FrameDropStage.SenderFloodGate,
    FrameDropStage.SenderDownscale,
    FrameDropStage.SenderEncode,
]);
const PlaybackKillStages = new Set<number>([
    FrameDropStage.ReceiverPull,
    FrameDropStage.ReceiverEncodedBuffer,
    FrameDropStage.ReceiverDecode,
]);
const StageNames = new Map<number, string>(
    Object.entries(FrameDropStage)
        .filter(([key]) => Number.isNaN(Number(key)))
        .map(([key, value]) => [value as number, key]));

export type VideoTraceKillKind = 'recording' | 'playback';
export type VideoTraceKillPeriod = number | 'now';
export type VideoTraceKillPeriodInput = number | string;

interface VideoKillOptions {
    avgPeriod: VideoTraceKillPeriod;
    stage: FrameDropStage;
    onKill?: () => void;
}

type FrameDropStageGlobal = typeof FrameDropStage;

type VideoTraceKillGlobal = typeof globalThis & {
    FrameDropStages?: FrameDropStageGlobal;
};

interface VideoKillState {
    options: VideoKillOptions;
    armedAtMs: number;
    lastSampleAtMs: number;
}

const videoKillOptions: {
    recording: VideoKillOptions | null;
    playback: VideoKillOptions | null;
} = {
    recording: null,
    playback: null,
};

const videoTraceKillGlobal = globalThis as VideoTraceKillGlobal;
videoTraceKillGlobal.FrameDropStages = FrameDropStage;

export function traceDrops<T extends DropTraced>(prevStage: FrameDropStage): PipeOperator<T, T> {
    return source => from(impl(source));

    async function* impl(source: AsyncIterable<T>): AsyncIterable<T> {
        let lastIndex: number | null = null;
        const maybeKill = createVideoTraceKiller(prevStage);
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
            // Kill AFTER yield: item is owned by the next operator now, so a throw can't leak it.
            maybeKill();
        }
    }
}

export function setVideoTraceKill(
    kind: VideoTraceKillKind,
    avgPeriod: VideoTraceKillPeriodInput,
    stage: number | string,
    onKill?: () => void,
): boolean {
    const validStages = kind === 'recording' ? RecordingKillStages : PlaybackKillStages;
    const normalizedAvgPeriod = avgPeriod === 'now' ? avgPeriod : Number(avgPeriod);
    const normalizedStage = Number(stage);
    if (
        (normalizedAvgPeriod !== 'now'
            && (!Number.isFinite(normalizedAvgPeriod) || normalizedAvgPeriod <= 0))
        || !validStages.has(normalizedStage)
    ) {
        videoKillOptions[kind] = null;
        console.info(`debugUI.killVideo${capitalize(kind)}: disabled`);
        return false;
    }

    videoKillOptions[kind] = {
        avgPeriod: normalizedAvgPeriod,
        stage: normalizedStage,
        onKill,
    };
    console.info(
        normalizedAvgPeriod === 'now'
            ? `debugUI.killVideo${capitalize(kind)}: enabled stage=${stageName(normalizedStage)} avgPeriod=now`
            : `debugUI.killVideo${capitalize(kind)}: enabled stage=${stageName(normalizedStage)} avgPeriod=${normalizedAvgPeriod}s`);
    return true;
}

export function isVideoTraceKillStageValid(kind: VideoTraceKillKind, stage: number): stage is FrameDropStage {
    const validStages = kind === 'recording' ? RecordingKillStages : PlaybackKillStages;
    return validStages.has(stage);
}

function createVideoTraceKiller(stage: FrameDropStage): () => void {
    const kind = stage >= FrameDropStage.ReceiverPull ? 'playback' : 'recording';
    let state: VideoKillState | null = null;

    return () => {
        const options = videoKillOptions[kind];
        if (options?.stage !== stage) {
            state = null;
            return;
        }

        const now = nowMs();
        if (state?.options !== options) {
            state = {
                options,
                armedAtMs: now,
                lastSampleAtMs: now,
            };
            if (options.avgPeriod === 'now') {
                videoKillOptions[kind] = null;
                options.onKill?.();
                throw new Error(
                    `debugUI.killVideo${capitalize(kind)} injected immediate failure at ${stageName(stage)}`);
            }
            return;
        }

        if (options.avgPeriod === 'now') {
            videoKillOptions[kind] = null;
            options.onKill?.();
            throw new Error(
                `debugUI.killVideo${capitalize(kind)} injected immediate failure at ${stageName(stage)}`);
        }

        const elapsedMs = Math.max(0, now - state.lastSampleAtMs);
        state.lastSampleAtMs = now;
        if (elapsedMs <= 0)
            return;

        const p = 1 - Math.exp(-elapsedMs / (options.avgPeriod * 1000));
        if (Math.random() >= p)
            return;

        const ageSec = (now - state.armedAtMs) / 1000;
        options.onKill?.();
        throw new Error(
            `debugUI.killVideo${capitalize(kind)} injected failure at ${stageName(stage)} ` +
            `after ${ageSec.toFixed(2)}s (avgPeriod=${options.avgPeriod}s)`);
    };
}

function nowMs(): number {
    return typeof performance !== 'undefined' ? performance.now() : Date.now();
}

function stageName(stage: number): string {
    return StageNames.get(stage) ?? `FrameDropStage(${stage})`;
}

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}
