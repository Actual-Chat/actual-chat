import type { Disposable } from 'disposable';
import {
    FrameDropStage,
    isVideoTraceKillStageValid,
    type VideoTraceKillPeriod,
    type VideoTraceKillPeriodInput,
    type VideoTraceKillKind,
} from './frame-drop-trace';

interface VideoTraceKillWorker {
    setTraceKill(avgPeriod: VideoTraceKillPeriod, stage: number): Promise<boolean>;
}

interface VideoTraceKillOptions {
    avgPeriod: VideoTraceKillPeriod;
    stage: FrameDropStage;
}

type VideoTraceKillGlobal = typeof globalThis & {
    FrameDropStages?: typeof FrameDropStage;
    __setVideoTraceKill?: (
        kind: string,
        avgPeriod: VideoTraceKillPeriodInput,
        stage: number | string,
    ) => boolean;
};

const workers: Record<VideoTraceKillKind, Set<VideoTraceKillWorker>> = {
    recording: new Set<VideoTraceKillWorker>(),
    playback: new Set<VideoTraceKillWorker>(),
};
const options: Record<VideoTraceKillKind, VideoTraceKillOptions | null> = {
    recording: null,
    playback: null,
};

const videoTraceKillGlobal = globalThis as VideoTraceKillGlobal;
videoTraceKillGlobal.FrameDropStages = FrameDropStage;
videoTraceKillGlobal.__setVideoTraceKill = setVideoTraceKill;

export function registerVideoTraceKillWorker(
    kind: VideoTraceKillKind,
    worker: VideoTraceKillWorker,
): Disposable {
    workers[kind].add(worker);
    pushTraceKillOptions(kind, worker);
    return {
        dispose: () => {
            workers[kind].delete(worker);
        },
    };
}

export function consumeVideoTraceKill(kind: VideoTraceKillKind): void {
    if (options[kind]?.avgPeriod !== 'now')
        return;

    options[kind] = null;
    for (const worker of workers[kind])
        pushTraceKillOptions(kind, worker);
    console.info(`debugUI.killVideo${capitalize(kind)}: consumed avgPeriod=now kill`);
}

function setVideoTraceKill(
    kind: string,
    avgPeriod: VideoTraceKillPeriodInput,
    stage: number | string,
): boolean {
    if (kind !== 'recording' && kind !== 'playback')
        return false;

    const normalizedAvgPeriod = avgPeriod === 'now' ? avgPeriod : Number(avgPeriod);
    const normalizedStage = Number(stage);
    const enabled = (
        normalizedAvgPeriod === 'now'
        || (Number.isFinite(normalizedAvgPeriod) && normalizedAvgPeriod > 0)
    ) && isVideoTraceKillStageValid(kind, normalizedStage);

    options[kind] = enabled
        ? { avgPeriod: normalizedAvgPeriod, stage: normalizedStage }
        : null;

    for (const worker of workers[kind])
        pushTraceKillOptions(kind, worker);

    console.info(enabled
        ? (normalizedAvgPeriod === 'now'
            ? `debugUI.killVideo${capitalize(kind)}: enabled stage=${stageName(normalizedStage)} avgPeriod=now`
            : `debugUI.killVideo${capitalize(kind)}: enabled stage=${stageName(normalizedStage)} avgPeriod=${normalizedAvgPeriod}s`)
        : `debugUI.killVideo${capitalize(kind)}: disabled`);
    return enabled;
}

function pushTraceKillOptions(kind: VideoTraceKillKind, worker: VideoTraceKillWorker): void {
    const current = options[kind];
    const avgPeriod = current?.avgPeriod ?? 0;
    const stage = current?.stage ?? FrameDropStage.None;
    void worker.setTraceKill(avgPeriod, stage).catch((e: unknown) => {
        console.warn(`debugUI.killVideo${capitalize(kind)}: worker sync failed`, e);
    });
}

function stageName(stage: number): string {
    return FrameDropStage[stage] ?? `FrameDropStage(${stage})`;
}

function capitalize(value: string): string {
    return value.charAt(0).toUpperCase() + value.slice(1);
}
